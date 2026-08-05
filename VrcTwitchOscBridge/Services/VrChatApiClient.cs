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
    private const int MaxWorldNameLength = 120;
    private const int MaxWorldLocationSuffixLength = 512;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public VrChatApiClient()
        : this(new HttpClientHandler())
    {
    }

    // This is the inspectable production path; the service owns both the handler and its client.
    internal VrChatApiClient(HttpClientHandler ownedHandler)
        : this(CreateOwnedHttpClient(ownedHandler), ownsHttpClient: true)
    {
    }

    // Injected clients remain caller-owned.
    internal VrChatApiClient(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private VrChatApiClient(HttpClient httpClient, bool ownsHttpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsHttpClient = ownsHttpClient;
        this.httpClient.BaseAddress = ApiBaseUri;
        this.httpClient.Timeout = DefaultRequestTimeout;

        foreach (var product in this.httpClient.DefaultRequestHeaders.UserAgent
                     .Where(product => string.Equals(
                         product.Product?.Name,
                         "CrystalRelayTwitchOsc",
                         StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.Remove(product);
        }

        this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelayTwitchOsc/desktop");

        foreach (var mediaType in this.httpClient.DefaultRequestHeaders.Accept
                     .Where(mediaType => string.Equals(
                         mediaType.MediaType,
                         "application/json",
                         StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            this.httpClient.DefaultRequestHeaders.Accept.Remove(mediaType);
        }

        this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static HttpClient CreateOwnedHttpClient(HttpClientHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handler.UseCookies = false;
        handler.AllowAutoRedirect = false;
        return new HttpClient(handler, disposeHandler: true);
    }

    public async Task<VrChatLoginResponse> LoginWithCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, VrChatApiRoutes.CurrentUser);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuthorization(username, password));

        using var response = await SendAsync(request, cancellationToken);
        var authCookie = ExtractAuthCookie(response);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Check for 2FA requirement in both success and error responses.
        // VRChat may return a 401 (instead of 200) when 2FA is required.
        var methods = TryExtractTwoFactorMethods(body);
        if (methods.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(authCookie))
            {
                throw new InvalidOperationException("VRChat did not return a reusable auth session for 2FA verification.");
            }

            return new VrChatLoginResponse(authCookie, null, methods);
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = ParseErrorMessageBody(body) ?? response.ReasonPhrase ?? $"HTTP {(int)response.StatusCode}";
            throw new VrChatApiException(response.StatusCode, message);
        }

        var payload = DeserializeBody<VrChatCurrentUserEnvelope>(body);
        if (payload is null)
        {
            throw new InvalidOperationException("VRChat returned an empty response.");
        }

        if (string.IsNullOrWhiteSpace(authCookie))
        {
            throw new InvalidOperationException("VRChat did not return a reusable auth session.");
        }

        return new VrChatLoginResponse(authCookie, ToAccountSettings(payload, authCookie), []);
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
                avatar.AuthorName,
                avatar.ThumbnailUrl,
                avatar.IsCurrentAvatar,
                avatar.IsUploaded,
                avatar.IsFavorited,
                avatar.IsLicensed,
                avatar.Platform,
                avatar.StyleTags,
                avatar.ContentTags,
                FavoriteGroupName: null))
            .ToArray();
    }



    public async Task<VrChatCurrentWorldLookupResult> GetCurrentWorldAsync(
        string authCookie,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(authCookie))
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }

        try
        {
            using var currentUserRequest = CreateRequest(HttpMethod.Get, VrChatApiRoutes.CurrentUser, authCookie);
            using var currentUserResponse = await SendAsync(currentUserRequest, cancellationToken);
            var currentUser = await ReadAsJsonAsync<VrChatCurrentUserEnvelope>(currentUserResponse, cancellationToken);
            var worldId = ResolveCurrentWorldId(currentUser);
            return string.IsNullOrWhiteSpace(worldId)
                ? VrChatCurrentWorldLookupResult.Unavailable
                : await GetWorldByIdAsync(worldId, authCookie, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }
        catch (Exception exception) when (IsUnavailableWorldLookupFailure(exception))
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }
    }

    public async Task<VrChatCurrentWorldLookupResult> GetWorldByIdAsync(
        string worldId,
        string? authCookie,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParseExactWorldId(worldId, out var validatedWorldId))
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }

        try
        {
            using var worldRequest = CreateRequest(HttpMethod.Get, VrChatApiRoutes.World(validatedWorldId), authCookie);
            using var worldResponse = await SendAsync(worldRequest, cancellationToken);
            var world = await ReadAsJsonAsync<VrChatWorldRecord>(worldResponse, cancellationToken);
            var worldName = world.Name?.Trim() ?? string.Empty;

            if (!TryParseExactWorldId(world.Id, out var responseWorldId)
                || !string.Equals(responseWorldId, validatedWorldId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(world.ReleaseStatus, "public", StringComparison.Ordinal)
                || worldName.Length == 0
                || worldName.Length > MaxWorldNameLength
                || !TryExtractUserId(world.AuthorId, out var authorId)
                || !string.Equals(world.AuthorId, authorId, StringComparison.Ordinal))
            {
                return VrChatCurrentWorldLookupResult.Unavailable;
            }

            var canonicalWorldId = responseWorldId.ToLowerInvariant();
            return VrChatCurrentWorldLookupResult.Available(
                canonicalWorldId,
                authorId,
                worldName,
                $"https://vrchat.com/home/world/{canonicalWorldId}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }
        catch (Exception exception) when (IsUnavailableWorldLookupFailure(exception))
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }
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

    public async Task<IReadOnlyList<FavoriteGroupRecord>> GetFavoriteGroupsAsync(
        string authCookie,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"{VrChatApiRoutes.FavoriteGroups}?type=avatar", authCookie);
        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsJsonAsync<List<FavoriteGroupRecord>>(response, cancellationToken) ?? [];
    }

    public async Task<List<FavoriteEntryRecord>> GetFavoriteEntriesAsync(
        string authCookie,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"{VrChatApiRoutes.ListFavorites}?type=avatar", authCookie);
        using var response = await SendAsync(request, cancellationToken);
        return await ReadAsJsonAsync<List<FavoriteEntryRecord>>(response, cancellationToken) ?? [];
    }

    public async Task<string?> AddFavoriteAsync(
        string authCookie,
        string avatarId,
        string groupTag,
        CancellationToken cancellationToken = default)
    {
        var body = new { type = "avatar", favoriteId = avatarId, tags = new[] { groupTag } };
        using var request = CreateRequest(HttpMethod.Post, VrChatApiRoutes.AddFavorite, authCookie);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var result = await ReadAsJsonAsync<FavoriteEntryRecord>(response, cancellationToken);
        return result?.Id;
    }

    public async Task<bool> RemoveFavoriteAsync(
        string authCookie,
        string favoriteId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, VrChatApiRoutes.RemoveFavorite(favoriteId), authCookie);
        using var response = await SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private static string ResolveCurrentWorldId(VrChatCurrentUserEnvelope payload)
    {
        if (TryParseWorldLocation(payload.TravelingToWorld, out _)
            || TryParseWorldLocation(payload.TravelingToLocation, out _)
            || TryParseWorldLocation(payload.Presence?.TravelingToWorld, out _))
        {
            return string.Empty;
        }

        if (TryParseWorldLocation(payload.Location, out var worldId)
            || TryParseWorldLocation(payload.Presence?.Location, out worldId)
            || TryParseWorldLocation(payload.Presence?.World, out worldId)
            || TryParseWorldLocation(payload.WorldId, out worldId))
        {
            return worldId;
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
        return TryParseWorldLocation(rawValue, out worldId);
    }

    private static bool TryParseExactWorldId(string? rawValue, out string worldId)
    {
        worldId = string.Empty;
        var candidate = rawValue ?? string.Empty;
        if (!candidate.StartsWith("wrld_", StringComparison.OrdinalIgnoreCase)
            || candidate.Length != "wrld_".Length + 36
            || !Guid.TryParseExact(candidate["wrld_".Length..], "D", out _))
        {
            return false;
        }

        worldId = candidate;
        return true;
    }

    private static bool TryParseWorldLocation(string? rawValue, out string worldId)
    {
        return TryParseWorldLocation(rawValue, out worldId, out _);
    }

    private static bool TryParseWorldLocation(
        string? rawValue,
        out string worldId,
        out string instanceLocationSuffix)
    {
        worldId = string.Empty;
        instanceLocationSuffix = string.Empty;
        if (TryParseExactWorldId(rawValue, out worldId))
        {
            return true;
        }

        var candidate = rawValue ?? string.Empty;
        var separatorIndex = candidate.IndexOf(':');
        if (separatorIndex != "wrld_".Length + 36
            || candidate.IndexOf(':', separatorIndex + 1) >= 0
            || !TryParseExactWorldId(candidate[..separatorIndex], out worldId))
        {
            worldId = string.Empty;
            return false;
        }

        instanceLocationSuffix = candidate[(separatorIndex + 1)..];
        if (!IsValidWorldLocationSuffix(instanceLocationSuffix))
        {
            worldId = string.Empty;
            instanceLocationSuffix = string.Empty;
            return false;
        }

        return true;
    }

    private static bool IsValidWorldLocationSuffix(string suffix)
    {
        if (suffix.Length == 0 || suffix.Length > MaxWorldLocationSuffixLength)
        {
            return false;
        }

        var segments = suffix.Split('~');
        if (segments[0].Length == 0
            || segments[0].Length > 10
            || segments[0].Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        for (var index = 1; index < segments.Length; index++)
        {
            if (!IsValidWorldLocationQualifier(segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidWorldLocationQualifier(string qualifier)
    {
        if (qualifier.Length == 0)
        {
            return false;
        }

        var openParenthesisIndex = qualifier.IndexOf('(');
        if (openParenthesisIndex < 0)
        {
            return IsAsciiIdentifier(qualifier);
        }

        if (openParenthesisIndex == 0
            || qualifier[^1] != ')'
            || qualifier.IndexOf('(', openParenthesisIndex + 1) >= 0)
        {
            return false;
        }

        var name = qualifier[..openParenthesisIndex];
        var value = qualifier[(openParenthesisIndex + 1)..^1];
        return IsAsciiIdentifier(name)
            && value.Length > 0
            && value.All(character =>
                character is >= '!' and <= '~'
                && character is not '(' and not ')' and not '~' and not '?' and not '/' and not '#' and not ':');
    }

    private static bool IsAsciiIdentifier(string candidate)
    {
        if (candidate.Length == 0
            || candidate.Length > 64
            || candidate[0] is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z'))
        {
            return false;
        }

        return candidate.All(character =>
            character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_');
    }

    internal static bool TryExtractUserId(string? rawValue, out string userId)
    {
        userId = string.Empty;
        var candidate = rawValue ?? string.Empty;
        if (!candidate.StartsWith("usr_", StringComparison.OrdinalIgnoreCase)
            || candidate.Length != "usr_".Length + 36
            || !Guid.TryParseExact(candidate["usr_".Length..], "D", out _))
        {
            return false;
        }

        userId = candidate;
        return true;
    }

    internal static string BuildLaunchUri(string location) => $"vrchat://launch?id={location?.Trim() ?? string.Empty}";

    internal static bool TryExtractInstanceId(string? rawValue, string worldId, out string instanceId)
    {
        instanceId = string.Empty;
        if (!TryParseExactWorldId(worldId, out var validatedWorldId)
            || !TryParseWorldLocation(rawValue, out var parsedWorldId, out var parsedInstanceId)
            || parsedInstanceId.Length == 0
            || !string.Equals(parsedWorldId, validatedWorldId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        instanceId = parsedInstanceId;
        return true;
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
                var platform = ResolvePlatform(avatar.UnityPackages);
                var (styleTags, contentTags) = ExtractTags(avatar.Tags);

                current = new MutableAvatar
                {
                    Id = avatar.Id,
                    Name = string.IsNullOrWhiteSpace(avatar.Name) ? avatar.Id : avatar.Name,
                    AuthorName = avatar.AuthorName?.Trim() ?? string.Empty,
                    ThumbnailUrl = avatar.ThumbnailImageUrl ?? avatar.ImageUrl,
                    IsCurrentAvatar = string.Equals(avatar.Id, currentAvatarId, StringComparison.Ordinal),
                    Platform = platform,
                    StyleTags = styleTags,
                    ContentTags = contentTags
                };
                merged[avatar.Id] = current;
            }

            current.Sources.Add(sourceLabel);
            current.IsCurrentAvatar |= string.Equals(avatar.Id, currentAvatarId, StringComparison.Ordinal);

            if (string.Equals(sourceLabel, "Uploaded", StringComparison.OrdinalIgnoreCase))
                current.IsUploaded = true;
            else if (string.Equals(sourceLabel, "Favorites", StringComparison.OrdinalIgnoreCase))
                current.IsFavorited = true;
            else if (string.Equals(sourceLabel, "Licensed", StringComparison.OrdinalIgnoreCase))
                current.IsLicensed = true;
        }
    }

    private static string ResolvePlatform(IReadOnlyList<UnityPackageRecord>? unityPackages)
    {
        if (unityPackages is null || unityPackages.Count == 0)
            return string.Empty;

        var hasPc = false;
        var hasQuest = false;
        foreach (var pkg in unityPackages)
        {
            var p = pkg.Platform?.Trim();
            if (string.Equals(p, "standalonewindows", StringComparison.OrdinalIgnoreCase))
                hasPc = true;
            else if (string.Equals(p, "android", StringComparison.OrdinalIgnoreCase))
                hasQuest = true;
        }

        if (hasPc && hasQuest) return "Both";
        if (hasPc) return "PC";
        if (hasQuest) return "Quest";
        return string.Empty;
    }

    private static (IReadOnlyList<string> StyleTags, IReadOnlyList<string> ContentTags) ExtractTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
            return (Array.Empty<string>(), Array.Empty<string>());

        var style = new List<string>();
        var content = new List<string>();
        foreach (var tag in tags)
        {
            if (tag.StartsWith("avatar_", StringComparison.Ordinal))
                style.Add(tag["avatar_".Length..]);
            else if (tag.StartsWith("content_", StringComparison.Ordinal))
                content.Add(tag["content_".Length..]);
        }

        return (style, content);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, string? authCookie)
    {
        var request = new HttpRequestMessage(method, relativePath);
        if (!string.IsNullOrWhiteSpace(authCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"auth={authCookie.Trim()}");
        }

        return request;
    }

    private static bool IsUnavailableWorldLookupFailure(Exception exception) =>
        exception is HttpRequestException
            or JsonException
            or InvalidOperationException
            or VrChatApiException;

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

    private static VrChatAccountSettings ToAccountSettings(VrChatCurrentUserEnvelope payload, string authCookie)
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
        return ParseErrorMessageBody(content) ?? response.ReasonPhrase ?? $"HTTP {(int)response.StatusCode}";
    }

    private static string? ParseErrorMessageBody(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
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

            return null;
        }
        catch
        {
            return content;
        }
    }

    private static IReadOnlyList<VrChatTwoFactorMethod> TryExtractTwoFactorMethods(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("requiresTwoFactorAuth", out var methodsElement)
                && methodsElement.ValueKind == JsonValueKind.Array)
            {
                var rawMethods = new List<string>();
                foreach (var item in methodsElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        rawMethods.Add(item.GetString() ?? string.Empty);
                    }
                }

                if (rawMethods.Count > 0)
                {
                    return ParseTwoFactorMethods(rawMethods);
                }
            }

            if (document.RootElement.TryGetProperty("error", out var errorElement)
                && errorElement.ValueKind == JsonValueKind.Object
                && errorElement.TryGetProperty("message", out var messageElement))
            {
                var message = messageElement.GetString() ?? string.Empty;
                if (message.Contains("2fa", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("two factor", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("two-factor", StringComparison.OrdinalIgnoreCase))
                {
                    return [VrChatTwoFactorMethod.Totp, VrChatTwoFactorMethod.EmailOtp, VrChatTwoFactorMethod.RecoveryCode];
                }
            }
        }
        catch
        {
        }

        return [];
    }

    private static T? DeserializeBody<T>(string body) where T : class
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
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

    private sealed class UnityPackageRecord
    {
        [JsonPropertyName("platform")]
        public string? Platform { get; set; }
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

        [JsonPropertyName("authorName")]
        public string? AuthorName { get; set; }

        [JsonPropertyName("authorId")]
        public string? AuthorId { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("unityPackages")]
        public List<UnityPackageRecord>? UnityPackages { get; set; }
    }

    public sealed class FavoriteGroupRecord
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    public sealed class FavoriteEntryRecord
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("favoriteId")]
        public string? FavoriteId { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    private sealed class MutableAvatar
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string AuthorName { get; init; } = string.Empty;

        public string? ThumbnailUrl { get; init; }

        public bool IsCurrentAvatar { get; set; }

        public bool IsUploaded { get; set; }

        public bool IsFavorited { get; set; }

        public bool IsLicensed { get; set; }

        public string Platform { get; init; } = string.Empty;

        public IReadOnlyList<string> StyleTags { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> ContentTags { get; init; } = Array.Empty<string>();

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
