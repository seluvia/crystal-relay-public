using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed record CashPaymentEvent(
    CashPaymentProvider Provider,
    string EventId,
    string UserDisplayName,
    decimal Amount,
    string CurrencyCode,
    string Message,
    DateTimeOffset ReceivedAt);

public sealed class CashPaymentProviderService : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(15);
    private static readonly Uri StreamElementsAstroUri = new("wss://astro.streamelements.com");
    private static readonly Uri StreamlabsSocketTokenUri = new("https://streamlabs.com/api/v2.0/socket/token");
    private static readonly JsonSerializerOptions RelayJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient = new();

    public async Task RunAsync(
        CashPaymentConnectionSnapshot connections,
        Func<CashPaymentEvent, CancellationToken, Task> handlePaymentAsync,
        Action<string> writeLog,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();

        if (connections.StreamElementsEnabled
            && !string.IsNullOrWhiteSpace(connections.StreamElementsJwtToken)
            && !string.IsNullOrWhiteSpace(connections.StreamElementsAccountId))
        {
            tasks.Add(RunProviderLoopAsync(
                "StreamElements",
                token => RunStreamElementsAsync(connections, handlePaymentAsync, writeLog, token),
                writeLog,
                cancellationToken));
        }

        if (connections.StreamlabsEnabled
            && !string.IsNullOrWhiteSpace(connections.StreamlabsAccessToken))
        {
            tasks.Add(RunProviderLoopAsync(
                "Streamlabs",
                token => RunStreamlabsAsync(connections, handlePaymentAsync, writeLog, token),
                writeLog,
                cancellationToken));
        }

        if (connections.KoFiEnabled
            && !string.IsNullOrWhiteSpace(connections.KoFiVerificationToken))
        {
            if (connections.KoFiConnectionMode == KoFiConnectionMode.HostedRelay
                && !string.IsNullOrWhiteSpace(connections.KoFiRelayChannelId)
                && !string.IsNullOrWhiteSpace(connections.KoFiRelayClientSecret))
            {
                tasks.Add(RunProviderLoopAsync(
                    "Ko-fi hosted relay",
                    token => RunKoFiHostedRelayAsync(connections, handlePaymentAsync, writeLog, token),
                    writeLog,
                    cancellationToken));
            }
            else
            {
                tasks.Add(RunProviderLoopAsync(
                    "Ko-fi",
                    token => RunKoFiWebhookAsync(connections, handlePaymentAsync, writeLog, token),
                    writeLog,
                    cancellationToken));
            }
        }

        if (tasks.Count == 0)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        await Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        await ValueTask.CompletedTask;
    }

    private static async Task RunProviderLoopAsync(
        string providerName,
        Func<CancellationToken, Task> runProviderAsync,
        Action<string> writeLog,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await runProviderAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                writeLog($"{providerName} payment listener stopped: {SensitiveTextSanitizer.Sanitize(ex.Message)}");
            }

            try
            {
                await Task.Delay(ReconnectDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }
    }

    private static async Task RunStreamElementsAsync(
        CashPaymentConnectionSnapshot connections,
        Func<CashPaymentEvent, CancellationToken, Task> handlePaymentAsync,
        Action<string> writeLog,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(StreamElementsAstroUri, cancellationToken);

        var subscribe = new
        {
            type = "subscribe",
            nonce = Guid.NewGuid().ToString("N"),
            data = new
            {
                topic = "channel.tips",
                room = connections.StreamElementsAccountId,
                token = connections.StreamElementsJwtToken,
                token_type = "jwt"
            }
        };
        await SendWebSocketTextAsync(socket, JsonSerializer.Serialize(subscribe), cancellationToken);
        writeLog("StreamElements payment listener connected.");

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var message = await ReceiveWebSocketTextAsync(socket, cancellationToken);
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            if (TryParseStreamElementsTip(message, out var paymentEvent))
            {
                await handlePaymentAsync(paymentEvent, cancellationToken);
            }
        }
    }

    private async Task RunStreamlabsAsync(
        CashPaymentConnectionSnapshot connections,
        Func<CashPaymentEvent, CancellationToken, Task> handlePaymentAsync,
        Action<string> writeLog,
        CancellationToken cancellationToken)
    {
        var socketToken = await FetchStreamlabsSocketTokenAsync(connections.StreamlabsAccessToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(socketToken))
        {
            throw new InvalidOperationException("Streamlabs did not return a socket token.");
        }

        var socketUri = new Uri($"wss://sockets.streamlabs.com/socket.io/?token={Uri.EscapeDataString(socketToken)}&EIO=3&transport=websocket");
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(socketUri, cancellationToken);
        writeLog("Streamlabs payment listener connected.");

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var frame = await ReceiveWebSocketTextAsync(socket, cancellationToken);
            if (string.IsNullOrWhiteSpace(frame))
            {
                continue;
            }

            if (frame == "2")
            {
                await SendWebSocketTextAsync(socket, "3", cancellationToken);
                continue;
            }

            if (frame.StartsWith('0'))
            {
                await SendWebSocketTextAsync(socket, "40", cancellationToken);
                continue;
            }

            if (TryParseStreamlabsDonation(frame, out var paymentEvents))
            {
                foreach (var paymentEvent in paymentEvents)
                {
                    await handlePaymentAsync(paymentEvent, cancellationToken);
                }
            }
        }
    }

    private async Task<string> FetchStreamlabsSocketTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, StreamlabsSocketTokenUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        if (TryGetString(root, "socket_token", out var socketToken)
            || TryGetString(root, "socketToken", out socketToken)
            || TryGetString(root, "token", out socketToken))
        {
            return socketToken;
        }

        if (root.TryGetProperty("data", out var data)
            && (TryGetString(data, "socket_token", out socketToken)
                || TryGetString(data, "socketToken", out socketToken)
                || TryGetString(data, "token", out socketToken)))
        {
            return socketToken;
        }

        return string.Empty;
    }

    private static async Task RunKoFiHostedRelayAsync(
        CashPaymentConnectionSnapshot connections,
        Func<CashPaymentEvent, CancellationToken, Task> handlePaymentAsync,
        Action<string> writeLog,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(BuildKoFiRelayWebSocketUri(connections), cancellationToken);

        var auth = new
        {
            type = "auth",
            protocolVersion = 1,
            clientSecret = connections.KoFiRelayClientSecret,
            verificationToken = connections.KoFiVerificationToken,
            appVersion = GetApplicationVersion()
        };
        await SendWebSocketTextAsync(socket, JsonSerializer.Serialize(auth, RelayJsonOptions), cancellationToken);
        writeLog("Ko-fi hosted relay connected.");

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var message = await ReceiveWebSocketTextAsync(socket, cancellationToken);
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            if (TryParseHostedKoFiRelayEvent(message, out var paymentEvent))
            {
                await handlePaymentAsync(paymentEvent, cancellationToken);
                await SendKoFiRelayAckAsync(socket, paymentEvent.EventId, cancellationToken);
                continue;
            }

            if (TryParseKoFiRelayStatus(message, out var statusMessage))
            {
                writeLog(statusMessage);
            }
        }
    }

    private static async Task RunKoFiWebhookAsync(
        CashPaymentConnectionSnapshot connections,
        Func<CashPaymentEvent, CancellationToken, Task> handlePaymentAsync,
        Action<string> writeLog,
        CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        var prefix = $"http://127.0.0.1:{connections.KoFiLocalPort}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        writeLog($"Ko-fi webhook listener started on http://127.0.0.1:{connections.KoFiLocalPort}{NormalizeWebhookPath(connections.KoFiWebhookPath)}.");

        using var stopRegistration = cancellationToken.Register(() =>
        {
            try
            {
                listener.Stop();
            }
            catch
            {
            }
        });

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            _ = Task.Run(
                () => HandleKoFiContextAsync(context, connections, handlePaymentAsync, cancellationToken),
                CancellationToken.None);
        }
    }

    private static async Task HandleKoFiContextAsync(
        HttpListenerContext context,
        CashPaymentConnectionSnapshot connections,
        Func<CashPaymentEvent, CancellationToken, Task> handlePaymentAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            var expectedPath = NormalizeWebhookPath(connections.KoFiWebhookPath);
            if (!string.Equals(context.Request.Url?.AbsolutePath ?? string.Empty, expectedPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken);
            if (TryParseKoFiWebhook(body, connections.KoFiVerificationToken, out var paymentEvent))
            {
                await handlePaymentAsync(paymentEvent, cancellationToken);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                await WriteHttpResponseAsync(context.Response, "ok", cancellationToken);
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            await WriteHttpResponseAsync(context.Response, "ignored", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        }
        catch
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    public static bool TryParseStreamElementsTip(string json, out CashPaymentEvent paymentEvent)
    {
        paymentEvent = default!;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetString(root, "topic", out var topic)
                || !string.Equals(topic, "channel.tips", StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("donation", out var donation)
                || !TryGetDecimal(donation, "amount", out var amount)
                || amount <= 0)
            {
                return false;
            }

            var eventId = TryGetString(data, "_id", out var id)
                ? id
                : TryGetString(root, "id", out var rootId)
                    ? rootId
                    : Guid.NewGuid().ToString("N");
            var user = donation.TryGetProperty("user", out var userElement)
                && TryGetString(userElement, "username", out var username)
                    ? username
                    : "StreamElements supporter";
            var currency = TryGetString(donation, "currency", out var currencyText) ? currencyText : string.Empty;
            var message = TryGetString(donation, "message", out var messageText) ? messageText : string.Empty;

            paymentEvent = new CashPaymentEvent(
                CashPaymentProvider.StreamElements,
                eventId,
                user,
                amount,
                NormalizeCurrencyCode(currency),
                message,
                DateTimeOffset.UtcNow);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseStreamlabsDonation(string socketFrame, out IReadOnlyList<CashPaymentEvent> paymentEvents)
    {
        paymentEvents = [];
        var json = socketFrame;
        if (socketFrame.StartsWith("42", StringComparison.Ordinal))
        {
            json = socketFrame[2..];
        }
        else if (socketFrame.StartsWith("42/", StringComparison.Ordinal))
        {
            var index = socketFrame.IndexOf('[', StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            json = socketFrame[index..];
        }
        else
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() < 2
                || !string.Equals(document.RootElement[0].GetString(), "event", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var eventData = document.RootElement[1];
            if (!TryGetString(eventData, "type", out var type)
                || !string.Equals(type, "donation", StringComparison.OrdinalIgnoreCase)
                || !eventData.TryGetProperty("message", out var messages)
                || messages.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var rootEventId = TryGetString(eventData, "event_id", out var eventId) ? eventId : string.Empty;
            var events = new List<CashPaymentEvent>();
            foreach (var donation in messages.EnumerateArray())
            {
                if (!TryGetDecimal(donation, "amount", out var amount) || amount <= 0)
                {
                    continue;
                }

                var id = TryGetString(donation, "_id", out var donationObjectId)
                    ? donationObjectId
                    : TryGetString(donation, "id", out var donationId)
                        ? donationId
                        : rootEventId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Guid.NewGuid().ToString("N");
                }

                var user = TryGetString(donation, "name", out var name)
                    ? name
                    : TryGetString(donation, "from", out var from)
                        ? from
                        : "Streamlabs supporter";
                var currency = TryGetString(donation, "currency", out var currencyText) ? currencyText : string.Empty;
                var message = TryGetString(donation, "message", out var messageText) ? messageText : string.Empty;

                events.Add(new CashPaymentEvent(
                    CashPaymentProvider.Streamlabs,
                    id,
                    user,
                    amount,
                    NormalizeCurrencyCode(currency),
                    message,
                    DateTimeOffset.UtcNow));
            }

            paymentEvents = events;
            return events.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseKoFiWebhook(string requestBody, string verificationToken, out CashPaymentEvent paymentEvent)
    {
        paymentEvent = default!;
        var json = ExtractKoFiJson(requestBody);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetString(root, "verification_token", out var actualToken)
                || !string.Equals(actualToken, verificationToken, StringComparison.Ordinal))
            {
                return false;
            }

            if (TryGetString(root, "type", out var type)
                && !string.Equals(type, "Donation", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "Tip", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryGetDecimal(root, "amount", out var amount) || amount <= 0)
            {
                return false;
            }

            var eventId = TryGetString(root, "message_id", out var messageId)
                ? messageId
                : TryGetString(root, "kofi_transaction_id", out var transactionId)
                    ? transactionId
                    : Guid.NewGuid().ToString("N");
            var user = TryGetString(root, "from_name", out var fromName) ? fromName : "Ko-fi supporter";
            var currency = TryGetString(root, "currency", out var currencyText) ? currencyText : string.Empty;
            var message = TryGetString(root, "message", out var messageText) ? messageText : string.Empty;

            paymentEvent = new CashPaymentEvent(
                CashPaymentProvider.KoFi,
                eventId,
                user,
                amount,
                NormalizeCurrencyCode(currency),
                message,
                DateTimeOffset.UtcNow);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseHostedKoFiRelayEvent(string json, out CashPaymentEvent paymentEvent)
    {
        paymentEvent = default!;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var type)
                || !string.Equals(type, "kofi.event", StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("event", out var eventElement)
                || !TryGetString(eventElement, "eventId", out var eventId)
                || !TryGetDecimal(eventElement, "amount", out var amount)
                || amount <= 0)
            {
                return false;
            }

            var user = TryGetString(eventElement, "userDisplayName", out var userText)
                ? userText
                : "Ko-fi supporter";
            var currency = TryGetString(eventElement, "currencyCode", out var currencyText)
                ? currencyText
                : string.Empty;
            var message = TryGetString(eventElement, "message", out var messageText)
                ? messageText
                : string.Empty;
            var receivedAt = TryGetString(eventElement, "receivedAt", out var receivedAtText)
                && DateTimeOffset.TryParse(
                    receivedAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var parsedReceivedAt)
                    ? parsedReceivedAt
                    : DateTimeOffset.UtcNow;

            paymentEvent = new CashPaymentEvent(
                CashPaymentProvider.KoFi,
                eventId,
                user,
                amount,
                NormalizeCurrencyCode(currency),
                message,
                receivedAt);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseKoFiRelayStatus(string json, out string statusMessage)
    {
        statusMessage = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var type))
            {
                return false;
            }

            statusMessage = type switch
            {
                "ready" => "Ko-fi hosted relay authenticated.",
                "heartbeat" => string.Empty,
                "error" => "Ko-fi hosted relay reported an error.",
                _ => string.Empty
            };
            return !string.IsNullOrWhiteSpace(statusMessage);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Uri BuildKoFiRelayWebSocketUri(CashPaymentConnectionSnapshot connections)
    {
        var baseUrl = string.IsNullOrWhiteSpace(connections.KoFiRelayBaseUrl)
            ? CashPaymentConnectionSettings.DefaultKoFiRelayBaseUrl
            : connections.KoFiRelayBaseUrl.Trim().TrimEnd('/');
        var baseUri = new Uri(baseUrl);
        var builder = new UriBuilder(baseUri)
        {
            Scheme = string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                ? "ws"
                : "wss",
            Path = $"/v1/kofi/connect/{Uri.EscapeDataString(connections.KoFiRelayChannelId.Trim())}",
            Query = string.Empty
        };
        return builder.Uri;
    }

    private static Task SendKoFiRelayAckAsync(ClientWebSocket socket, string eventId, CancellationToken cancellationToken)
    {
        var ack = new
        {
            type = "ack",
            eventId
        };
        return SendWebSocketTextAsync(socket, JsonSerializer.Serialize(ack, RelayJsonOptions), cancellationToken);
    }

    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(CashPaymentProviderService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var plusIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return plusIndex > 0 ? informationalVersion[..plusIndex] : informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "Unknown";
    }

    private static string ExtractKoFiJson(string requestBody)
    {
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return string.Empty;
        }

        var trimmed = requestBody.Trim();
        if (trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "data", StringComparison.OrdinalIgnoreCase))
            {
                return WebUtility.UrlDecode(parts[1]) ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static async Task<string> ReceiveWebSocketTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return string.Empty;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private static Task SendWebSocketTextAsync(ClientWebSocket socket, string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task WriteHttpResponseAsync(HttpListenerResponse response, string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeCurrencyCode(string currencyCode)
    {
        var normalized = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length <= 8 ? normalized : normalized[..8];
    }

    private static string NormalizeWebhookPath(string path)
    {
        var trimmed = path?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "/kofi";
        }

        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : $"/{trimmed}";
    }
}
