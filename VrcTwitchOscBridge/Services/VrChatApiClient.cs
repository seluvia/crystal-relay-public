using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed class VrChatApiClient : IDisposable
{
    private static readonly Uri ApiBaseUri = new("https://api.vrchat.cloud/api/1/");
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(18);
    private static readonly char[] WorldLocationSeparators = [':', '~', '?', '/', ' '];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient = new()
    {
        BaseAddress = ApiBaseUri,
        Timeout = DefaultRequestTimeout
    };

    public VrChatApiClient()
    {
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelayTwitchOsc/desktop");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<VrChatLoginResponse> LoginWithCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, VrChatApiRoutes.CurrentUser);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuthorization(username, password));

        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<VrChatCurrentUserEnvelope>(response, cancellationToken);
        var authCookie = ExtractAuthCookie(response);

        if (string.IsNullOrWhiteSpace(authCookie))
        {
            throw new InvalidOperationException("VRChat did not return a reusable auth session.");
        }

        var methods = ParseTwoFactorMethods(payload.RequiresTwoFactorAuth);
        if (methods.Count > 0)
        {
            return new VrChatLoginResponse(authCookie, null, methods);
        }

        return new VrChatLoginResponse(authCookie, ToAccountSettings(payload), []);
    }

    public async Task<VrChatAccountSettings> CompleteTwoFactorAsync(
        string authCookie,
        VrChatTwoFactorMethod method,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Enter the VRChat 2FA code before continuing.");
        }

        using var request = CreateRequest(HttpMethod.Post, GetTwoFactorVerifyPath(method), authCookie);
        request.Content = JsonContent.Create(new VrChatCodeRequest
        {
            Code = code.Trim()
        });

        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await GetCurrentUserAsync(authCookie, cancellationToken);
    }

    public async Task<VrChatAccountSettings> GetCurrentUserAsync(
        string authCookie,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, VrChatApiRoutes.CurrentUser, authCookie);
        using var response = await SendAsync(request, cancellationToken);
        var payload = await ReadAsJsonAsync<VrChatCurrentUserEnvelope>(response, cancellationToken);

        var methods = ParseTwoFactorMethods(payload.RequiresTwoFactorAuth);
        if (methods.Count > 0)
        {
            throw new InvalidOperationException("VRChat still requires 2FA before the avatar list can load.");
        }

        return ToAccountSettings(payload, authCookie);
    }

    public async Task LogoutAsync(string authCookie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authCookie))
        {
            return;
        }

        using var request = CreateRequest(HttpMethod.Put, VrChatApiRoutes.Logout, authCookie);
        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<VrChatAvatarSummary>> GetSelectableAvatarsAsync(
        string authCookie,
        string currentAvatarId,
        CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<string, MutableAvatar>(StringComparer.Ordinal);
        var uploadedTask = GetPagedAvatarsAsync(VrChatApiRoutes.UploadedAvatars, authCookie, cancellationToken);
        var favoritesTask = GetPagedAvatarsAsync(VrChatApiRoutes.FavoriteAvatars, authCookie, cancellationToken);
        var licensedTask = GetPagedAvatarsAsync(VrChatApiRoutes.LicensedAvatars, authCookie, cancellationToken);

        await Task.WhenAll(uploadedTask, favoritesTask, licensedTask);

        MergeAvatars(merged, uploadedTask.Result, "Uploaded", currentAvatarId);
        MergeAvatars(merged, favoritesTask.Result, "Favorites", currentAvatarId);
        MergeAvatars(merged, licensedTask.Result, "Licensed", currentAvatarId);

        return merged.Values
            .OrderByDescending(avatar => avatar.IsCurrentAvatar)
            .ThenBy(avatar => avatar.Name, StringComparer.OrdinalIgnoreCase)
            .Select(avatar => new VrChatAvatarSummary(
                avatar.Id,
                avatar.Name,
                string.Join(" / ", avatar.Sources.OrderBy(source => source, StringComparer.OrdinalIgnoreCase)),
                avatar.IsCurrentAvatar,
                avatar.ThumbnailUrl))
            .ToArray();
    }

    public async Task<VrChatCurrentWorldLookupResult> GetCurrentWorldAsync(
        string authCookie,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authCookie))
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }

        using var currentUserRequest = CreateRequest(HttpMethod.Get, VrChatApiRoutes.CurrentUser, authCookie);
        using var currentUserResponse = await SendAsync(currentUserRequest, cancellationToken);
        var currentUser = await ReadAsJsonAsync<VrChatCurrentUserEnvelope>(currentUserResponse, cancellationToken);
        var worldId = ResolveCurrentWorldId(currentUser);
        if (string.IsNullOrWhiteSpace(worldId))
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }

        using var worldRequest = CreateRequest(HttpMethod.Get, VrChatApiRoutes.World(worldId), authCookie);
        using var worldResponse = await SendAsync(worldRequest, cancellationToken);
        var world = await ReadAsJsonAsync<VrChatWorldRecord>(worldResponse, cancellationToken);
        if (string.IsNullOrWhiteSpace(world.Id)
            || !string.Equals(world.Id.Trim(), worldId, StringComparison.Ordinal)
            || !string.Equals(world.ReleaseStatus?.Trim(), "public", StringComparison.OrdinalIgnoreCase))
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }

        var worldName = string.IsNullOrWhiteSpace(world.Name)
            ? worldId
            : world.Name.Trim();
        return VrChatCurrentWorldLookupResult.Available(
            worldId,
            world.AuthorId?.Trim() ?? string.Empty,
            worldName,
            $"https://vrchat.com/home/world/{worldId}");
    }

    public async Task<VrChatCurrentLocationLookupResult> GetCurrentLocationAsync(
        string authCookie,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authCookie))
        {
            return VrChatCurrentLocationLookupResult.Unavailable("VRChat avatar access is not connected.");
        }

        using var currentUserRequest = CreateRequest(HttpMethod.Get, VrChatApiRoutes.CurrentUser, authCookie);
        using var currentUserResponse = await SendAsync(currentUserRequest, cancellationToken);
        var currentUser = await ReadAsJsonAsync<VrChatCurrentUserEnvelope>(currentUserResponse, cancellationToken);
        var location = ResolveCurrentLocation(currentUser);
        if (!TryExtractWorldId(location, out var worldId)
            || !TryExtractInstanceId(location, worldId, out var instanceId))
        {
            return VrChatCurrentLocationLookupResult.Unavailable(
                "Crystal Relay could not read a current VRChat world instance to rejoin.");
        }

        var normalizedLocation = $"{worldId}:{instanceId}";
        return VrChatCurrentLocationLookupResult.Available(
            worldId,
            instanceId,
            normalizedLocation,
            BuildLaunchUri(normalizedLocation),
            "api");
    }

    public async Task<bool> InviteMyselfToInstanceAsync(
        string authCookie,
        string location,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authCookie) || string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        using var request = CreateRequest(HttpMethod.Post, VrChatApiRoutes.InviteMyselfToInstance(location), authCookie);
        using var response = await SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public void Dispose() => httpClient.Dispose();

    private static string ResolveCurrentWorldId(VrChatCurrentUserEnvelope payload)
    {
        string?[] candidates =
        [
            payload.Presence?.World,
            payload.WorldId,
            payload.Location,
            payload.TravelingToWorld,
            payload.TravelingToLocation,
            payload.Presence?.TravelingToWorld
        ];

        foreach (var candidate in candidates)
        {
            if (TryExtractWorldId(candidate, out var worldId))
            {
                return worldId;
            }
        }

        return string.Empty;
    }

    private static string ResolveCurrentLocation(VrChatCurrentUserEnvelope payload)
    {
        string?[] candidates =
        [
            payload.Location,
            payload.TravelingToLocation,
            payload.Presence?.Location
        ];

        foreach (var candidate in candidates)
        {
            var normalized = candidate?.Trim() ?? string.Empty;
            if (TryExtractWorldId(normalized, out _)
                && normalized.Contains(':', StringComparison.Ordinal))
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    internal static bool TryExtractWorldId(string? rawValue, out string worldId)
    {
        worldId = string.Empty;
        var candidate = rawValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var separatorIndex = candidate.IndexOfAny(WorldLocationSeparators);
        if (separatorIndex >= 0)
        {
            candidate = candidate[..separatorIndex];
        }

        if (!candidate.StartsWith("wrld_", StringComparison.OrdinalIgnoreCase)
            || candidate.Length != "wrld_".Length + 36
            || !Guid.TryParse(candidate["wrld_".Length..], out _))
        {
            return false;
        }

        worldId = candidate;
        return true;
    }

    internal static string BuildLaunchUri(string location) => $"vrchat://launch?id={location?.Trim() ?? string.Empty}";

    internal static bool TryExtractInstanceId(string? rawValue, string worldId, out string instanceId)
    {
        instanceId = string.Empty;
        var candidate = rawValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)
            || string.IsNullOrWhiteSpace(worldId)
            || !candidate.StartsWith(worldId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = candidate.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex == candidate.Length - 1)
        {
            return false;
        }

        instanceId = candidate[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(instanceId);
    }

    private async Task<IReadOnlyList<VrChatAvatarRecord>> GetPagedAvatarsAsync(
        string relativePath,
        string authCookie,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        const int maxPages = 10;

        var avatars = new List<VrChatAvatarRecord>();
        for (var page = 0; page < maxPages; page++)
        {
            var separator = relativePath.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var requestPath = $"{relativePath}{separator}n={pageSize}&offset={page * pageSize}";

            using var request = CreateRequest(HttpMethod.Get, requestPath, authCookie);
            using var response = await SendAsync(request, cancellationToken);
            var pageItems = await ReadAsJsonAsync<List<VrChatAvatarRecord>>(response, cancellationToken);
            if (pageItems.Count == 0)
            {
                break;
            }

            avatars.AddRange(pageItems.Where(avatar => !string.IsNullOrWhiteSpace(avatar.Id)));

            if (pageItems.Count < pageSize)
            {
                break;
            }
        }

        return avatars;
    }

    private static void MergeAvatars(
        IDictionary<string, MutableAvatar> merged,
        IReadOnlyList<VrChatAvatarRecord> avatars,
        string sourceLabel,
        string currentAvatarId)
    {
        foreach (var avatar in avatars)
        {
            if (!merged.TryGetValue(avatar.Id, out var current))
            {
                current = new MutableAvatar
                {
                    Id = avatar.Id,
                    Name = string.IsNullOrWhiteSpace(avatar.Name) ? avatar.Id : avatar.Name,
                    ThumbnailUrl = avatar.ThumbnailImageUrl ?? avatar.ImageUrl,
                    IsCurrentAvatar = string.Equals(avatar.Id, currentAvatarId, StringComparison.Ordinal)
                };
                merged[avatar.Id] = current;
            }

            current.Sources.Add(sourceLabel);
            current.IsCurrentAvatar |= string.Equals(avatar.Id, currentAvatarId, StringComparison.Ordinal);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, string authCookie)
    {
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.TryAddWithoutValidation("Cookie", $"auth={authCookie.Trim()}");
        return request;
    }

    private static string BuildBasicAuthorization(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Enter both the VRChat username and password before continuing.");
        }

        var raw = $"{username.Trim()}:{password}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VrChatApiException(
                System.Net.HttpStatusCode.RequestTimeout,
                "VRChat took too long to respond. Try again in a moment.");
        }
    }

    private static string? ExtractAuthCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            if (!value.StartsWith("auth=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = value.IndexOf(';');
            var cookiePart = separatorIndex >= 0 ? value[..separatorIndex] : value;
            var equalsIndex = cookiePart.IndexOf('=');
            if (equalsIndex < 0 || equalsIndex == cookiePart.Length - 1)
            {
                continue;
            }

            return cookiePart[(equalsIndex + 1)..].Trim();
        }

        return null;
    }

    private static IReadOnlyList<VrChatTwoFactorMethod> ParseTwoFactorMethods(List<string>? rawMethods)
    {
        if (rawMethods is null || rawMethods.Count == 0)
        {
            return [];
        }

        var methods = new List<VrChatTwoFactorMethod>();
        foreach (var rawMethod in rawMethods)
        {
            switch (rawMethod?.Trim().ToLowerInvariant())
            {
                case "totp":
                    methods.Add(VrChatTwoFactorMethod.Totp);
                    break;
                case "emailotp":
                    methods.Add(VrChatTwoFactorMethod.EmailOtp);
                    break;
                case "otp":
                    methods.Add(VrChatTwoFactorMethod.RecoveryCode);
                    break;
            }
        }

        return methods.Distinct().ToArray();
    }

    private static string GetTwoFactorVerifyPath(VrChatTwoFactorMethod method) => method switch
    {
        VrChatTwoFactorMethod.EmailOtp => "auth/twofactorauth/emailotp/verify",
        VrChatTwoFactorMethod.RecoveryCode => "auth/twofactorauth/otp/verify",
        _ => "auth/twofactorauth/totp/verify"
    };

    private static VrChatAccountSettings ToAccountSettings(VrChatCurrentUserEnvelope payload, string authCookie = "")
    {
        return new VrChatAccountSettings
        {
            AuthCookie = authCookie,
            UserId = payload.Id ?? string.Empty,
            DisplayName = payload.DisplayName ?? "VRChat user",
            CurrentAvatarId = payload.CurrentAvatar ?? string.Empty
        };
    }

    private static async Task<T> ReadAsJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("VRChat returned an empty response.");
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
        throw new VrChatApiException(response.StatusCode, message);
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
            if (document.RootElement.TryGetProperty("error", out var errorElement)
                && errorElement.ValueKind == JsonValueKind.Object
                && errorElement.TryGetProperty("message", out var nestedMessage))
            {
                return nestedMessage.GetString() ?? content;
            }

            if (document.RootElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString() ?? content;
            }

            return content;
        }
        catch
        {
            return content;
        }
    }

    public sealed record VrChatLoginResponse(
        string AuthCookie,
        VrChatAccountSettings? Account,
        IReadOnlyList<VrChatTwoFactorMethod> RequiredTwoFactorMethods);

    private sealed class VrChatCodeRequest
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
    }

    private sealed class VrChatCurrentUserEnvelope
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("currentAvatar")]
        public string? CurrentAvatar { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("worldId")]
        public string? WorldId { get; set; }

        [JsonPropertyName("travelingToWorld")]
        public string? TravelingToWorld { get; set; }

        [JsonPropertyName("travelingToLocation")]
        public string? TravelingToLocation { get; set; }

        [JsonPropertyName("presence")]
        public VrChatPresenceRecord? Presence { get; set; }

        [JsonPropertyName("requiresTwoFactorAuth")]
        public List<string>? RequiresTwoFactorAuth { get; set; }
    }

    private sealed class VrChatPresenceRecord
    {
        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("world")]
        public string? World { get; set; }

        [JsonPropertyName("travelingToWorld")]
        public string? TravelingToWorld { get; set; }
    }

    private sealed class VrChatWorldRecord
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("authorId")]
        public string? AuthorId { get; set; }

        [JsonPropertyName("releaseStatus")]
        public string ReleaseStatus { get; set; } = string.Empty;
    }

    private sealed class VrChatAvatarRecord
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("imageUrl")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("thumbnailImageUrl")]
        public string? ThumbnailImageUrl { get; set; }
    }

    private sealed class MutableAvatar
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? ThumbnailUrl { get; init; }

        public bool IsCurrentAvatar { get; set; }

        public HashSet<string> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record VrChatCurrentWorldLookupResult(
    bool IsAvailable,
    string WorldId,
    string WorldAuthorId,
    string WorldName,
    string WorldUrl)
{
    public static VrChatCurrentWorldLookupResult Unavailable { get; } = new(false, string.Empty, string.Empty, string.Empty, string.Empty);

    public static VrChatCurrentWorldLookupResult Available(string worldId, string worldAuthorId, string worldName, string worldUrl) =>
        new(true, worldId, worldAuthorId, worldName, worldUrl);
}

public sealed record VrChatCurrentLocationLookupResult(
    bool IsAvailable,
    string WorldId,
    string InstanceId,
    string Location,
    string LaunchUri,
    string Source,
    string FailureReason)
{
    public static VrChatCurrentLocationLookupResult Unavailable(string failureReason) =>
        new(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, failureReason);

    public static VrChatCurrentLocationLookupResult Available(
        string worldId,
        string instanceId,
        string location,
        string launchUri,
        string source) =>
        new(true, worldId, instanceId, location, launchUri, source, string.Empty);
}
