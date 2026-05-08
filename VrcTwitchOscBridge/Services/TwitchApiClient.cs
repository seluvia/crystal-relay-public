using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Small Twitch HTTP wrapper used by Crystal Relay.
/// It handles OAuth/device flow, Helix lookups, EventSub management, reward management,
/// chat sends, and public About-page profile lookups.
/// </summary>
public sealed class TwitchApiClient : IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly Regex TwitterImageMetaRegex = new(
        "<meta\\s+name=[\"']twitter:image[\"']\\s+content=[\"'](?<url>[^\"']+)[\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OgImageMetaRegex = new(
        "<meta\\s+property=[\"']og:image[\"']\\s+content=[\"'](?<url>[^\"']+)[\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly TimeSpan BroadcasterRefreshReuseWindow = TimeSpan.FromMinutes(2);
    private static readonly SemaphoreSlim BroadcasterRefreshGate = new(1, 1);
    private static string lastBroadcasterRefreshInput = string.Empty;
    private static TokenExchangeResponse? lastBroadcasterRefreshResponse;
    private static DateTimeOffset lastBroadcasterRefreshAt = DateTimeOffset.MinValue;

    private readonly HttpClient httpClient = new()
    {
        Timeout = DefaultRequestTimeout
    };

    // Twitch device flow used for the broadcaster and bot login buttons inside Crystal Relay.
    public async Task<DeviceCodeResponse> StartDeviceAuthorizationAsync(
        string clientId,
        IEnumerable<string> scopes,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/device")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["scopes"] = string.Join(' ', scopes)
            }!)
        };

        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsJsonAsync<DeviceCodeResponse>(response, cancellationToken);
    }

    public async Task<TokenExchangeResponse> ExchangeDeviceCodeAsync(
        string clientId,
        IEnumerable<string> scopes,
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["scopes"] = string.Join(' ', scopes),
                ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }!)
        };

        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsJsonAsync<TokenExchangeResponse>(response, cancellationToken);
    }

    public async Task<TokenExchangeResponse> RefreshAccessTokenAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        return await RefreshAccessTokenCoreAsync(clientId, refreshToken, cancellationToken);
    }

    public async Task<TokenExchangeResponse> RefreshBroadcasterAccessTokenAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        await BroadcasterRefreshGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (lastBroadcasterRefreshResponse is not null
                && string.Equals(lastBroadcasterRefreshInput, refreshToken, StringComparison.Ordinal)
                && lastBroadcasterRefreshAt.Add(BroadcasterRefreshReuseWindow) > now)
            {
                return CloneTokenExchangeResponse(lastBroadcasterRefreshResponse);
            }

            var response = await RefreshAccessTokenCoreAsync(clientId, refreshToken, cancellationToken);
            lastBroadcasterRefreshInput = refreshToken;
            lastBroadcasterRefreshResponse = CloneTokenExchangeResponse(response);
            lastBroadcasterRefreshAt = now;
            return response;
        }
        finally
        {
            BroadcasterRefreshGate.Release();
        }
    }

    private async Task<TokenExchangeResponse> RefreshAccessTokenCoreAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }!)
        };

        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsJsonAsync<TokenExchangeResponse>(response, cancellationToken);
    }

    private static TokenExchangeResponse CloneTokenExchangeResponse(TokenExchangeResponse response)
    {
        return new TokenExchangeResponse
        {
            AccessToken = response.AccessToken,
            ExpiresIn = response.ExpiresIn,
            RefreshToken = response.RefreshToken,
            Scope = [.. response.Scope],
            TokenType = response.TokenType
        };
    }

    public async Task<TokenValidationResponse?> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.TryAddWithoutValidation("Authorization", $"OAuth {accessToken}");

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        return await ReadAsJsonAsync<TokenValidationResponse>(response, cancellationToken);
    }

    public async Task<UserResponse?> GetUserAsync(
        string accessToken,
        string clientId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(HttpMethod.Get, $"https://api.twitch.tv/helix/users?id={Uri.EscapeDataString(userId)}", accessToken, clientId);
        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<UserListResponse>(response, cancellationToken);
        return payload.Data.FirstOrDefault();
    }

    public async Task<IReadOnlyList<UserResponse>> GetUsersByLoginsAsync(
        string accessToken,
        string clientId,
        IEnumerable<string> logins,
        CancellationToken cancellationToken = default)
    {
        var requestedLogins = logins
            .Where(login => !string.IsNullOrWhiteSpace(login))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requestedLogins.Length == 0)
        {
            return [];
        }

        var query = string.Join(
            "&",
            requestedLogins.Select(login => $"login={Uri.EscapeDataString(login)}"));

        using var request = CreateHelixRequest(
            HttpMethod.Get,
            $"https://api.twitch.tv/helix/users?{query}",
            accessToken,
            clientId);
        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<UserListResponse>(response, cancellationToken);
        return payload.Data;
    }

    public async Task<bool> IsBroadcasterLiveAsync(
        string accessToken,
        string clientId,
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(
            HttpMethod.Get,
            $"https://api.twitch.tv/helix/streams?user_id={Uri.EscapeDataString(broadcasterId)}",
            accessToken,
            clientId);
        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<StreamListResponse>(response, cancellationToken);
        return payload.Data.Count > 0;
    }

    public async Task<IReadOnlySet<string>> GetLiveStreamUserIdsAsync(
        string accessToken,
        string clientId,
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var requestedUserIds = userIds
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Select(userId => userId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requestedUserIds.Length == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var query = string.Join(
            "&",
            requestedUserIds.Select(userId => $"user_id={Uri.EscapeDataString(userId)}"));

        using var request = CreateHelixRequest(
            HttpMethod.Get,
            $"https://api.twitch.tv/helix/streams?{query}",
            accessToken,
            clientId);
        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<StreamListResponse>(response, cancellationToken);
        return payload.Data
            .Where(stream => !string.IsNullOrWhiteSpace(stream.UserId))
            .Select(stream => stream.UserId)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Custom reward helpers are used by Crystal Relay's managed-reward system.
    // They let the app mirror redeem state to Twitch instead of relying only on internal rule checks.
    public async Task<IReadOnlyList<CustomRewardResponse>> GetCustomRewardsAsync(
        string accessToken,
        string clientId,
        string broadcasterId,
        CancellationToken cancellationToken = default,
        bool onlyManageableRewards = false)
    {
        var rewardsUrl = new StringBuilder("https://api.twitch.tv/helix/channel_points/custom_rewards");
        rewardsUrl.Append("?broadcaster_id=");
        rewardsUrl.Append(Uri.EscapeDataString(broadcasterId));
        if (onlyManageableRewards)
        {
            rewardsUrl.Append("&only_manageable_rewards=true");
        }

        using var request = CreateHelixRequest(
            HttpMethod.Get,
            rewardsUrl.ToString(),
            accessToken,
            clientId);

        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<CustomRewardListResponse>(response, cancellationToken);
        return payload.Data;
    }

    public async Task<CustomRewardResponse> CreateCustomRewardAsync(
        string accessToken,
        string clientId,
        string broadcasterId,
        string title,
        int cost,
        bool isEnabled,
        int cooldownSeconds,
        string backgroundColor,
        CancellationToken cancellationToken = default,
        string prompt = "",
        bool isUserInputRequired = false)
    {
        using var request = CreateHelixRequest(
            HttpMethod.Post,
            $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(broadcasterId)}",
            accessToken,
            clientId);
        request.Content = JsonContent.Create(BuildCustomRewardPayload(title, cost, isEnabled, cooldownSeconds, backgroundColor, prompt, isUserInputRequired));

        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<CustomRewardListResponse>(response, cancellationToken);
        return payload.Data.FirstOrDefault()
            ?? throw new InvalidOperationException("Twitch created the reward, but returned no reward details.");
    }

    public async Task<CustomRewardResponse> UpdateCustomRewardAsync(
        string accessToken,
        string clientId,
        string broadcasterId,
        string rewardId,
        string title,
        int cost,
        bool isEnabled,
        int cooldownSeconds,
        string backgroundColor,
        CancellationToken cancellationToken = default,
        string prompt = "",
        bool isUserInputRequired = false)
    {
        using var request = CreateHelixRequest(
            new HttpMethod("PATCH"),
            $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&id={Uri.EscapeDataString(rewardId)}",
            accessToken,
            clientId);
        request.Content = JsonContent.Create(BuildCustomRewardPayload(title, cost, isEnabled, cooldownSeconds, backgroundColor, prompt, isUserInputRequired));

        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<CustomRewardListResponse>(response, cancellationToken);
        return payload.Data.FirstOrDefault()
            ?? throw new InvalidOperationException("Twitch updated the reward, but returned no reward details.");
    }

    public async Task<CustomRewardResponse> UpdateCustomRewardVisibilityAsync(
        string accessToken,
        string clientId,
        string broadcasterId,
        string rewardId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(
            new HttpMethod("PATCH"),
            $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&id={Uri.EscapeDataString(rewardId)}",
            accessToken,
            clientId);
        request.Content = JsonContent.Create(new CustomRewardVisibilityMutationPayload
        {
            IsEnabled = isEnabled
        });

        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<CustomRewardListResponse>(response, cancellationToken);
        return payload.Data.FirstOrDefault()
            ?? throw new InvalidOperationException("Twitch updated the reward visibility, but returned no reward details.");
    }

    public async Task DeleteCustomRewardAsync(
        string accessToken,
        string clientId,
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(
            HttpMethod.Delete,
            $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&id={Uri.EscapeDataString(rewardId)}",
            accessToken,
            clientId);

        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatBadgeSetResponse>> GetGlobalChatBadgesAsync(
        string accessToken,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(
            HttpMethod.Get,
            "https://api.twitch.tv/helix/chat/badges/global",
            accessToken,
            clientId);

        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<ChatBadgeSetListResponse>(response, cancellationToken);
        return payload.Data;
    }

    public async Task<IReadOnlyList<ChatBadgeSetResponse>> GetChannelChatBadgesAsync(
        string accessToken,
        string clientId,
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(
            HttpMethod.Get,
            $"https://api.twitch.tv/helix/chat/badges?broadcaster_id={Uri.EscapeDataString(broadcasterId)}",
            accessToken,
            clientId);

        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<ChatBadgeSetListResponse>(response, cancellationToken);
        return payload.Data;
    }

    public async Task<ChatEmoteSetListResponse> GetEmoteSetsAsync(
        string accessToken,
        string clientId,
        IReadOnlyList<string> emoteSetIds,
        CancellationToken cancellationToken = default)
    {
        if (emoteSetIds is null || emoteSetIds.Count == 0)
        {
            return new ChatEmoteSetListResponse();
        }

        var distinctSetIds = emoteSetIds
            .Where(emoteSetId => !string.IsNullOrWhiteSpace(emoteSetId))
            .Select(emoteSetId => emoteSetId.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(25)
            .ToArray();
        if (distinctSetIds.Length == 0)
        {
            return new ChatEmoteSetListResponse();
        }

        var query = string.Join(
            "&",
            distinctSetIds.Select(emoteSetId => $"emote_set_id={Uri.EscapeDataString(emoteSetId)}"));
        using var request = CreateHelixRequest(
            HttpMethod.Get,
            $"https://api.twitch.tv/helix/chat/emotes/set?{query}",
            accessToken,
            clientId);

        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsJsonAsync<ChatEmoteSetListResponse>(response, cancellationToken);
    }

    public async Task<ChatEmoteSetListResponse> GetGlobalChatEmotesAsync(
        string accessToken,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(
            HttpMethod.Get,
            "https://api.twitch.tv/helix/chat/emotes/global",
            accessToken,
            clientId);

        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsJsonAsync<ChatEmoteSetListResponse>(response, cancellationToken);
    }

    public async Task<ChatEmoteSetListResponse> GetChannelChatEmotesAsync(
        string accessToken,
        string clientId,
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(
            HttpMethod.Get,
            $"https://api.twitch.tv/helix/chat/emotes?broadcaster_id={Uri.EscapeDataString(broadcasterId)}",
            accessToken,
            clientId);

        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsJsonAsync<ChatEmoteSetListResponse>(response, cancellationToken);
    }

    public async Task<ChatEmoteSetListResponse> GetUserChatEmotesAsync(
        string accessToken,
        string clientId,
        string userId,
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        var combined = new ChatEmoteSetListResponse();
        var after = string.Empty;

        do
        {
            var query = new StringBuilder($"user_id={Uri.EscapeDataString(userId)}");
            if (!string.IsNullOrWhiteSpace(broadcasterId))
            {
                query.Append("&broadcaster_id=");
                query.Append(Uri.EscapeDataString(broadcasterId));
            }

            if (!string.IsNullOrWhiteSpace(after))
            {
                query.Append("&after=");
                query.Append(Uri.EscapeDataString(after));
            }

            using var request = CreateHelixRequest(
                HttpMethod.Get,
                $"https://api.twitch.tv/helix/chat/emotes/user?{query}",
                accessToken,
                clientId);

            using var response = await SendAsync(request, cancellationToken);
            var page = await ReadAsJsonAsync<ChatEmoteSetListResponse>(response, cancellationToken);
            if (string.IsNullOrWhiteSpace(combined.Template))
            {
                combined.Template = page.Template;
            }

            combined.Data.AddRange(page.Data);
            after = page.Pagination.Cursor;
        }
        while (!string.IsNullOrWhiteSpace(after));

        return combined;
    }

    public async Task<IReadOnlyList<EventSubSubscriptionInfo>> GetEventSubSubscriptionsAsync(
        string accessToken,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(HttpMethod.Get, "https://api.twitch.tv/helix/eventsub/subscriptions", accessToken, clientId);
        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<EventSubSubscriptionListResponse>(response, cancellationToken);
        return payload.Data;
    }

    public async Task<bool> CreateEventSubSubscriptionAsync(
        string accessToken,
        string clientId,
        string sessionId,
        string type,
        string version,
        object condition,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            type,
            version,
            condition,
            transport = new
            {
                method = "websocket",
                session_id = sessionId
            }
        };

        using var request = CreateHelixRequest(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions", accessToken, clientId);
        request.Content = JsonContent.Create(payload);

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async Task DeleteEventSubSubscriptionAsync(
        string accessToken,
        string clientId,
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(HttpMethod.Delete, $"https://api.twitch.tv/helix/eventsub/subscriptions?id={Uri.EscapeDataString(subscriptionId)}", accessToken, clientId);
        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SendChatMessageAsync(
        string accessToken,
        string clientId,
        string broadcasterId,
        string senderId,
        string message,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateHelixRequest(HttpMethod.Post, "https://api.twitch.tv/helix/chat/messages", accessToken, clientId);
        request.Content = JsonContent.Create(new
        {
            broadcaster_id = broadcasterId,
            sender_id = senderId,
            message
        });

        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<string> GetPublicChannelProfileImageUrlAsync(
        string twitchLogin,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(twitchLogin))
        {
            return string.Empty;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.twitch.tv/{Uri.EscapeDataString(twitchLogin.Trim())}");
        request.Headers.UserAgent.ParseAdd("CrystalRelayTwitchOsc/desktop");

        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        var imageUrl = ExtractPublicMetaImageUrl(html, TwitterImageMetaRegex);
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            return imageUrl;
        }

        return ExtractPublicMetaImageUrl(html, OgImageMetaRegex);
    }

    public void Dispose() => httpClient.Dispose();

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TwitchApiException(
                HttpStatusCode.RequestTimeout,
                "Twitch took too long to respond. Try again in a moment.");
        }
    }

    private static CustomRewardMutationPayload BuildCustomRewardPayload(
        string title,
        int cost,
        bool isEnabled,
        int cooldownSeconds,
        string backgroundColor,
        string prompt,
        bool isUserInputRequired)
    {
        var normalizedCooldown = Math.Max(0, cooldownSeconds);
        return new CustomRewardMutationPayload
        {
            Title = title,
            Cost = Math.Max(1, cost),
            IsEnabled = isEnabled,
            IsGlobalCooldownEnabled = normalizedCooldown > 0,
            GlobalCooldownSeconds = normalizedCooldown,
            BackgroundColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(backgroundColor),
            Prompt = prompt?.Trim() ?? string.Empty,
            IsUserInputRequired = isUserInputRequired
        };
    }

    // Shared Helix request builder so Twitch auth headers stay consistent across all endpoints.
    private static HttpRequestMessage CreateHelixRequest(HttpMethod method, string uri, string accessToken, string clientId)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        request.Headers.TryAddWithoutValidation("Client-Id", clientId);
        return request;
    }

    // Central JSON reader for Twitch responses. All non-success cases are normalized into TwitchApiException first.
    private static async Task<T> ReadAsJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("Twitch returned an empty response.");
        }

        return payload;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ReadErrorMessageAsync(response, cancellationToken);
        throw new TwitchApiException(response.StatusCode, message, GetRetryAfterUtc(response));
    }

    private static DateTimeOffset? GetRetryAfterUtc(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfterDelta)
        {
            return DateTimeOffset.UtcNow.Add(retryAfterDelta);
        }

        if (response.Headers.RetryAfter?.Date is { } retryAfterDate)
        {
            return retryAfterDate;
        }

        if (response.Headers.TryGetValues("Ratelimit-Reset", out var resetValues))
        {
            var resetValue = resetValues.FirstOrDefault();
            if (long.TryParse(resetValue, out var resetUnixSeconds))
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeSeconds(resetUnixSeconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
        }

        return null;
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return response.ReasonPhrase ?? $"HTTP {(int)response.StatusCode}";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString() ?? content;
            }

            if (document.RootElement.TryGetProperty("error_description", out var descriptionElement))
            {
                return descriptionElement.GetString() ?? content;
            }

            return content;
        }
        catch
        {
            return content;
        }
    }

    private static string ExtractPublicMetaImageUrl(string html, Regex regex)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var match = regex.Match(html);
        if (!match.Success)
        {
            return string.Empty;
        }

        var imageUrl = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim());
        if (string.IsNullOrWhiteSpace(imageUrl)
            || imageUrl.Contains("twitch_logo", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return imageUrl;
    }

    public sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = string.Empty;

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = string.Empty;
    }

    public sealed class TokenExchangeResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("scope")]
        public List<string> Scope { get; set; } = [];

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }

    public sealed class TokenValidationResponse
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("scopes")]
        public List<string> Scopes { get; set; } = [];

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    public sealed class UserListResponse
    {
        public List<UserResponse> Data { get; set; } = [];
    }

    public sealed class UserResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("profile_image_url")]
        public string ProfileImageUrl { get; set; } = string.Empty;
    }

    public sealed class StreamListResponse
    {
        [JsonPropertyName("data")]
        public List<StreamResponse> Data { get; set; } = [];
    }

    public sealed class StreamResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;
    }

    public sealed class CustomRewardListResponse
    {
        [JsonPropertyName("data")]
        public List<CustomRewardResponse> Data { get; set; } = [];
    }

    public sealed class CustomRewardResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("cost")]
        public int Cost { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("is_global_cooldown_enabled")]
        public bool IsGlobalCooldownEnabled { get; set; }

        [JsonPropertyName("global_cooldown_seconds")]
        public int? GlobalCooldownSeconds { get; set; }

        [JsonPropertyName("background_color")]
        public string BackgroundColor { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("is_user_input_required")]
        public bool IsUserInputRequired { get; set; }
    }

    private sealed class CustomRewardMutationPayload
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("cost")]
        public int Cost { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("is_global_cooldown_enabled")]
        public bool IsGlobalCooldownEnabled { get; set; }

        [JsonPropertyName("global_cooldown_seconds")]
        public int GlobalCooldownSeconds { get; set; }

        [JsonPropertyName("background_color")]
        public string BackgroundColor { get; set; } = ManagedRewardPresentation.ReadyBackgroundColor;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("is_user_input_required")]
        public bool IsUserInputRequired { get; set; }
    }

    private sealed class CustomRewardVisibilityMutationPayload
    {
        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }
    }

    public sealed class ChatBadgeSetListResponse
    {
        [JsonPropertyName("data")]
        public List<ChatBadgeSetResponse> Data { get; set; } = [];
    }

    public sealed class ChatEmoteSetListResponse
    {
        [JsonPropertyName("data")]
        public List<ChatEmoteResponse> Data { get; set; } = [];

        [JsonPropertyName("template")]
        public string Template { get; set; } = string.Empty;

        [JsonPropertyName("pagination")]
        public PaginationResponse Pagination { get; set; } = new();
    }

    public sealed class ChatEmoteResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("emote_set_id")]
        public string EmoteSetId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("images")]
        public ChatEmoteImagesResponse Images { get; set; } = new();

        [JsonPropertyName("format")]
        public List<string> Format { get; set; } = [];

        [JsonPropertyName("scale")]
        public List<string> Scale { get; set; } = [];

        [JsonPropertyName("theme_mode")]
        public List<string> ThemeMode { get; set; } = [];
    }

    public sealed class ChatEmoteImagesResponse
    {
        [JsonPropertyName("url_1x")]
        public string Url1x { get; set; } = string.Empty;

        [JsonPropertyName("url_2x")]
        public string Url2x { get; set; } = string.Empty;

        [JsonPropertyName("url_4x")]
        public string Url4x { get; set; } = string.Empty;
    }

    public sealed class PaginationResponse
    {
        [JsonPropertyName("cursor")]
        public string Cursor { get; set; } = string.Empty;
    }

    public sealed class ChatBadgeSetResponse
    {
        [JsonPropertyName("set_id")]
        public string SetId { get; set; } = string.Empty;

        [JsonPropertyName("versions")]
        public List<ChatBadgeVersionResponse> Versions { get; set; } = [];
    }

    public sealed class ChatBadgeVersionResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("image_url_1x")]
        public string ImageUrl1x { get; set; } = string.Empty;

        [JsonPropertyName("image_url_2x")]
        public string ImageUrl2x { get; set; } = string.Empty;

        [JsonPropertyName("image_url_4x")]
        public string ImageUrl4x { get; set; } = string.Empty;
    }

    public sealed class EventSubSubscriptionListResponse
    {
        public List<EventSubSubscriptionInfo> Data { get; set; } = [];
    }

    public sealed class EventSubSubscriptionInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("condition")]
        public EventSubConditionInfo Condition { get; set; } = new();

        [JsonPropertyName("transport")]
        public EventSubTransportInfo Transport { get; set; } = new();
    }

    public sealed class EventSubConditionInfo
    {
        [JsonPropertyName("broadcaster_user_id")]
        public string BroadcasterUserId { get; set; } = string.Empty;

        [JsonPropertyName("to_broadcaster_user_id")]
        public string ToBroadcasterUserId { get; set; } = string.Empty;
    }

    public sealed class EventSubTransportInfo
    {
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;
    }
}
