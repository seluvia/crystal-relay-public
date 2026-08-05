using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatCurrentWorldTests
{
    private const string WorldA = "wrld_11111111-1111-1111-1111-111111111111";
    private const string WorldB = "wrld_22222222-2222-2222-2222-222222222222";
    private const string WorldC = "wrld_33333333-3333-3333-3333-333333333333";
    private const string WorldD = "wrld_44444444-4444-4444-4444-444444444444";
    private const string AuthorA = "usr_aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string SyntheticAuthCookie = "synthetic-test-cookie";
    private const string RealisticInstanceSuffix =
        "12345~hidden(usr_aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa)~region(us)";

    [Fact]
    public async Task PublicWorld_WithNoOccupantsOrInstances_ReturnsNameCreatorAndWorldPage()
    {
        using var fixture = CreateFixture(WorldJson(
            WorldA,
            AuthorA,
            "Quiet Test World",
            occupants: 0,
            includeEmptyInstances: true));

        var result = await fixture.Service.GetWorldByIdAsync(WorldA, authCookie: null);

        Assert.True(result.IsAvailable);
        Assert.Equal(WorldA, result.WorldId);
        Assert.Equal(AuthorA, result.WorldAuthorId);
        Assert.Equal("Quiet Test World", result.WorldName);
        Assert.Equal($"https://vrchat.com/home/world/{WorldA}", result.WorldUrl);
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"/api/1/worlds/{WorldA}", request.Uri.AbsolutePath);
        Assert.False(request.Headers.ContainsKey("Cookie"));
    }

    [Fact]
    public async Task CurrentWorld_RefreshesAuthUserAndNeverCallsInstanceRoutes()
    {
        using var fixture = CreateFixture(
            CurrentUserJson(location: $"{WorldA}:12345~region(us)"),
            WorldJson(WorldA, AuthorA, "Current Test World", occupants: 0, includeEmptyInstances: true));

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.True(result.IsAvailable);
        Assert.Collection(
            fixture.Handler.Requests,
            request => Assert.Equal("/api/1/auth/user", request.Uri.AbsolutePath),
            request => Assert.Equal($"/api/1/worlds/{WorldA}", request.Uri.AbsolutePath));
        Assert.All(fixture.Handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(string.Empty, request.Uri.Query);
            Assert.Equal($"auth={SyntheticAuthCookie}", Assert.Single(request.Headers["Cookie"]));
            Assert.Equal("application/json", Assert.Single(request.Headers["Accept"]));
            Assert.Equal("CrystalRelayTwitchOsc/desktop", Assert.Single(request.Headers["User-Agent"]));
        });
        Assert.DoesNotContain(fixture.Handler.Requests, request =>
            request.Uri.AbsolutePath.Contains("/instances", StringComparison.OrdinalIgnoreCase)
            || request.Uri.AbsolutePath.Contains("/worlds/active", StringComparison.OrdinalIgnoreCase)
            || request.Uri.Query.Contains("search", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TravelingTarget_SuppressesStaleCurrentFields()
    {
        using var fixture = CreateFixture(CurrentUserJson(
            location: $"{WorldA}:12345",
            presenceLocation: $"{WorldB}:23456",
            presenceWorld: WorldC,
            worldId: WorldD,
            travelingToWorld: WorldB));

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.False(result.IsAvailable);
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal("/api/1/auth/user", request.Uri.AbsolutePath);
    }

    [Theory]
    [MemberData(nameof(TravelingFieldPayloads))]
    public async Task CurrentWorld_EachTravelingFieldSuppressesStaleCurrentFields(string payload)
    {
        using var fixture = CreateFixture(
            payload,
            WorldJson(WorldA, AuthorA, "Stale Current World"));

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.False(result.IsAvailable);
        Assert.Equal("/api/1/auth/user", Assert.Single(fixture.Handler.Requests).Uri.AbsolutePath);
    }

    [Theory]
    [MemberData(nameof(ApprovedFieldOrderPayloads))]
    public async Task CurrentWorld_UsesApprovedFieldOrder(string payload)
    {
        using var fixture = CreateFixture(payload, WorldJson(WorldA, AuthorA, "Ordered Test World"));

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.True(result.IsAvailable);
        Assert.Equal(WorldA, result.WorldId);
        Assert.Equal($"/api/1/worlds/{WorldA}", fixture.Handler.Requests[1].Uri.AbsolutePath);
    }

    [Theory]
    [MemberData(nameof(InvalidWorldMetadataPayloads))]
    public async Task InvalidWorldMetadata_ReturnsUnavailable(string payload)
    {
        using var fixture = CreateFixture(payload);

        var result = await fixture.Service.GetWorldByIdAsync(WorldA, SyntheticAuthCookie);

        Assert.False(result.IsAvailable);
        Assert.Equal(string.Empty, result.WorldId);
        Assert.Equal(string.Empty, result.WorldAuthorId);
        Assert.Equal(string.Empty, result.WorldName);
        Assert.Equal(string.Empty, result.WorldUrl);
        AssertOnlyWorldMetadataRequest(fixture, WorldA);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrld_not-a-uuid")]
    [InlineData(WorldA + ":12345")]
    [InlineData(WorldA + ":")]
    [InlineData(WorldA + "?garbage")]
    [InlineData(WorldA + "~region(us)")]
    [InlineData(WorldA + "/12345")]
    [InlineData(" " + WorldA)]
    [InlineData(WorldA + " ")]
    public async Task DirectWorldLookup_InvalidWorldIdDoesNotSendRequest(string worldId)
    {
        using var fixture = CreateFixture(Array.Empty<string>());

        var result = await fixture.Service.GetWorldByIdAsync(worldId, SyntheticAuthCookie);

        Assert.False(result.IsAvailable);
        Assert.Empty(fixture.Handler.Requests);
        AssertNoFallbackRoutes(fixture.Handler.Requests);
    }

    [Fact]
    public async Task DirectWorldLookup_MatchesResponseIdCaseInsensitively()
    {
        const string lowercaseWorldId = "wrld_abcdefab-cdef-abcd-efab-cdefabcdefab";
        var uppercaseWorldId = lowercaseWorldId.ToUpperInvariant();
        using var fixture = CreateFixture(WorldJson(lowercaseWorldId, AuthorA, "Case Test World"));

        var result = await fixture.Service.GetWorldByIdAsync(uppercaseWorldId, authCookie: null);

        Assert.True(result.IsAvailable);
        Assert.Equal(lowercaseWorldId, result.WorldId);
        Assert.Equal($"https://vrchat.com/home/world/{lowercaseWorldId}", result.WorldUrl);
        AssertOnlyWorldMetadataRequest(fixture, uppercaseWorldId);
    }

    [Fact]
    public async Task DirectWorldLookup_TrimsCanonicalNameAtTheSupportedBound()
    {
        var boundedName = new string('n', 120);
        using var fixture = CreateFixture(WorldJson(WorldA, AuthorA, $"  {boundedName}  "));

        var result = await fixture.Service.GetWorldByIdAsync(WorldA, authCookie: null);

        Assert.True(result.IsAvailable);
        Assert.Equal(boundedName, result.WorldName);
    }

    [Fact]
    public async Task DirectWorldLookup_HttpFailureReturnsUnavailable()
    {
        using var fixture = CreateFixture(Response(HttpStatusCode.ServiceUnavailable, "{}"));

        var result = await fixture.Service.GetWorldByIdAsync(WorldA, SyntheticAuthCookie);

        Assert.False(result.IsAvailable);
        AssertOnlyWorldMetadataRequest(fixture, WorldA);
    }

    [Fact]
    public async Task DirectWorldLookup_TimeoutReturnsUnavailable()
    {
        using var fixture = CreateFixture((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Synthetic timeout.")));

        var result = await fixture.Service.GetWorldByIdAsync(WorldA, SyntheticAuthCookie);

        Assert.False(result.IsAvailable);
        AssertOnlyWorldMetadataRequest(fixture, WorldA);
    }

    [Fact]
    public async Task DirectWorldLookup_CallerCancellationPropagates()
    {
        using var fixture = CreateFixture(WorldJson(WorldA, AuthorA, "Cancelled Test World"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.GetWorldByIdAsync(
                WorldA,
                SyntheticAuthCookie,
                cancellation.Token));

        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task CurrentWorld_CallerCancellationPropagates()
    {
        using var fixture = CreateFixture(CurrentUserJson(location: WorldA));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie, cancellation.Token));

        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task CurrentWorld_TimeoutReturnsUnavailable()
    {
        using var fixture = CreateFixture((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Synthetic timeout.")));

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.False(result.IsAvailable);
        AssertOnlyAuthUserRequest(fixture);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task CurrentWorld_MalformedAuthUserReturnsUnavailable(string payload)
    {
        using var fixture = CreateFixture(payload);

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.False(result.IsAvailable);
        AssertOnlyAuthUserRequest(fixture);
    }

    [Theory]
    [MemberData(nameof(ValidLocationPayloads))]
    public async Task CurrentWorld_LocationFieldsAcceptRealisticValues(string payload)
    {
        using var fixture = CreateFixture(payload, WorldJson(WorldA, AuthorA, "Location Test World"));

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.True(result.IsAvailable);
        Assert.Equal(WorldA, result.WorldId);
        AssertCurrentWorldRequestSequence(fixture, WorldA);
    }

    [Theory]
    [MemberData(nameof(MalformedHigherPriorityCurrentPayloads))]
    public async Task CurrentWorld_MalformedHigherPriorityFieldFallsThrough(string payload)
    {
        using var fixture = CreateFixture(payload, WorldJson(WorldA, AuthorA, "Fallback Test World"));

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.True(result.IsAvailable);
        Assert.Equal(WorldA, result.WorldId);
        AssertCurrentWorldRequestSequence(fixture, WorldA);
    }

    [Theory]
    [MemberData(nameof(MalformedOnlyWorldIdPayloads))]
    public async Task CurrentWorld_MalformedOnlyWorldIdReturnsUnavailable(string payload)
    {
        using var fixture = CreateFixture(payload);

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.False(result.IsAvailable);
        AssertOnlyAuthUserRequest(fixture);
    }

    [Theory]
    [MemberData(nameof(MalformedTravelPayloads))]
    public async Task CurrentWorld_MalformedTravelFieldDoesNotSuppressCurrentWorld(string payload)
    {
        using var fixture = CreateFixture(payload, WorldJson(WorldA, AuthorA, "Travel Fallback World"));

        var result = await fixture.Service.GetCurrentWorldAsync(SyntheticAuthCookie);

        Assert.True(result.IsAvailable);
        Assert.Equal(WorldA, result.WorldId);
        AssertCurrentWorldRequestSequence(fixture, WorldA);
    }

    [Fact]
    public void InjectableClient_ConfiguresExpectedDefaultsExactlyOnce()
    {
        using var handler = new QueueHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        using var service = new VrChatApiClient(httpClient);

        Assert.Equal(new Uri("https://api.vrchat.cloud/api/1/"), httpClient.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(18), httpClient.Timeout);
        Assert.Equal("application/json", Assert.Single(httpClient.DefaultRequestHeaders.Accept).MediaType);
        Assert.Equal(
            "CrystalRelayTwitchOsc/desktop",
            Assert.Single(httpClient.DefaultRequestHeaders.UserAgent).ToString());
    }

    [Fact]
    public void OwnedHandler_DisablesAutomaticCookiesAndRedirects()
    {
        var handler = new HttpClientHandler();
        using var service = new VrChatApiClient(handler);

        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task DirectWorldLookup_ExplicitAuthCookieRemainsRequestScoped()
    {
        using var fixture = CreateFixture(
            WorldJson(WorldA, AuthorA, "Authenticated Test World"),
            WorldJson(WorldA, AuthorA, "Anonymous Test World"));

        var authenticated = await fixture.Service.GetWorldByIdAsync(WorldA, SyntheticAuthCookie);
        var anonymous = await fixture.Service.GetWorldByIdAsync(WorldA, authCookie: null);

        Assert.True(authenticated.IsAvailable);
        Assert.True(anonymous.IsAvailable);
        Assert.Collection(
            fixture.Handler.Requests,
            request => Assert.Equal(
                $"auth={SyntheticAuthCookie}",
                Assert.Single(request.Headers["Cookie"])),
            request => Assert.False(request.Headers.ContainsKey("Cookie")));
    }

    [Fact]
    public void InjectableClient_NormalizesDuplicateRequiredHeaders()
    {
        using var handler = new QueueHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json", 0));
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/plain"));
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelayTwitchOsc/legacy");
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelayTwitchOsc/desktop");
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OtherClient/1.0");

        using var firstService = new VrChatApiClient(httpClient);
        using var secondService = new VrChatApiClient(httpClient);

        var jsonAccept = Assert.Single(httpClient.DefaultRequestHeaders.Accept, header =>
            string.Equals(header.MediaType, "application/json", StringComparison.OrdinalIgnoreCase));
        Assert.Null(jsonAccept.Quality);
        Assert.Contains(httpClient.DefaultRequestHeaders.Accept, header =>
            string.Equals(header.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase));

        var crystalRelayUserAgent = Assert.Single(httpClient.DefaultRequestHeaders.UserAgent, product =>
            string.Equals(product.Product?.Name, "CrystalRelayTwitchOsc", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("desktop", crystalRelayUserAgent.Product?.Version);
        Assert.Contains(httpClient.DefaultRequestHeaders.UserAgent, product =>
            string.Equals(product.Product?.Name, "OtherClient", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InjectableClient_RemainsOwnedByCallerAfterServiceDisposal()
    {
        using var handler = new QueueHttpMessageHandler();
        handler.Enqueue("{}");
        using var httpClient = new HttpClient(handler);
        var service = new VrChatApiClient(httpClient);

        service.Dispose();
        using var response = await httpClient.GetAsync("ownership-probe");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static IEnumerable<object[]> TravelingFieldPayloads()
    {
        yield return [CurrentUserJson(
            location: $"{WorldA}:12345",
            presenceLocation: $"{WorldB}:23456",
            presenceWorld: WorldC,
            worldId: WorldD,
            travelingToWorld: $"{WorldB}:{RealisticInstanceSuffix}")];
        yield return [CurrentUserJson(
            location: $"{WorldA}:12345",
            presenceLocation: $"{WorldB}:23456",
            presenceWorld: WorldC,
            worldId: WorldD,
            travelingToLocation: $"{WorldB}:{RealisticInstanceSuffix}")];
        yield return [CurrentUserJson(
            location: $"{WorldA}:12345",
            presenceLocation: $"{WorldB}:23456",
            presenceWorld: WorldC,
            worldId: WorldD,
            presenceTravelingToWorld: $"{WorldB}:{RealisticInstanceSuffix}")];
    }

    public static IEnumerable<object[]> ApprovedFieldOrderPayloads()
    {
        yield return [CurrentUserJson(
            location: $"{WorldA}:12345~region(us)",
            presenceLocation: WorldB,
            presenceWorld: WorldC,
            worldId: WorldD)];
        yield return [CurrentUserJson(
            location: "private",
            presenceLocation: $"{WorldA}:23456",
            presenceWorld: WorldB,
            worldId: WorldC)];
        yield return [CurrentUserJson(
            location: "offline",
            presenceLocation: "private",
            presenceWorld: WorldA,
            worldId: WorldB)];
        yield return [CurrentUserJson(
            location: "offline",
            presenceLocation: "private",
            presenceWorld: "",
            worldId: WorldA)];
    }

    public static IEnumerable<object[]> InvalidWorldMetadataPayloads()
    {
        yield return [WorldJson(WorldB, AuthorA, "Wrong World")];
        yield return [WorldJson(WorldA, AuthorA, "Private World", releaseStatus: "private")];
        yield return [WorldJson(WorldA, AuthorA, "Wrong Case", releaseStatus: "Public")];
        yield return [WorldJson(WorldA, AuthorA, "Padded Status", releaseStatus: "public ")];
        yield return [WorldJson(WorldA, AuthorA, "")];
        yield return [WorldJson(WorldA, AuthorA, "   ")];
        yield return [WorldJson(WorldA, AuthorA, new string('n', 121))];
        yield return [WorldJson(WorldA, "", "Missing Creator")];
        yield return [WorldJson(WorldA, "usr_not-a-uuid", "Invalid Creator")];
        yield return [WorldJson(WorldA, $"{AuthorA}:suffix", "Suffixed Creator")];
        yield return [WorldJson(WorldA, $" {AuthorA}", "Padded Creator")];
        yield return [WorldJson($"{WorldA}:12345", AuthorA, "Suffixed World ID")];
        yield return [WorldJson($"{WorldA}?garbage", AuthorA, "Query World ID")];
        yield return [WorldJson($"{WorldA}~region(us)", AuthorA, "Tilde World ID")];
        yield return [WorldJson($"{WorldA}/12345", AuthorA, "Path World ID")];
        yield return [WorldJson($"{WorldA} ", AuthorA, "Padded World ID")];
        yield return ["{"];
        yield return ["null"];
        yield return ["[]"];
    }

    public static IEnumerable<object[]> ValidLocationPayloads()
    {
        yield return [CurrentUserJson(
            location: $"{WorldA}:{RealisticInstanceSuffix}",
            presenceLocation: WorldB,
            presenceWorld: WorldC,
            worldId: WorldD)];
        yield return [CurrentUserJson(
            location: "offline",
            presenceLocation: $"{WorldA}:{RealisticInstanceSuffix}",
            presenceWorld: WorldB,
            worldId: WorldC)];
        yield return [CurrentUserJson(
            location: "offline",
            presenceLocation: "private",
            presenceWorld: $"{WorldA}:{RealisticInstanceSuffix}",
            worldId: WorldB)];
        yield return [CurrentUserJson(
            location: "offline",
            presenceLocation: "private",
            presenceWorld: "",
            worldId: $"{WorldA}:{RealisticInstanceSuffix}")];
    }

    public static IEnumerable<object[]> MalformedHigherPriorityCurrentPayloads()
    {
        foreach (var value in MalformedLocationValues())
        {
            yield return [CurrentUserJson(location: value, presenceLocation: WorldA)];
            yield return [CurrentUserJson(
                location: "offline",
                presenceLocation: value,
                presenceWorld: WorldA)];
        }

        foreach (var value in MalformedLocationValues())
        {
            yield return [CurrentUserJson(
                location: "offline",
                presenceLocation: "private",
                presenceWorld: value,
                worldId: WorldA)];
        }
    }

    public static IEnumerable<object[]> MalformedOnlyWorldIdPayloads()
    {
        foreach (var value in MalformedLocationValues())
        {
            yield return [CurrentUserJson(
                location: "offline",
                presenceLocation: "private",
                presenceWorld: "",
                worldId: value)];
        }
    }

    public static IEnumerable<object[]> MalformedTravelPayloads()
    {
        foreach (var value in MalformedLocationValues())
        {
            yield return [CurrentUserJson(location: WorldA, travelingToWorld: value)];
            yield return [CurrentUserJson(location: WorldA, travelingToLocation: value)];
            yield return [CurrentUserJson(location: WorldA, presenceTravelingToWorld: value)];
        }
    }

    private static IEnumerable<string> MalformedLocationValues()
    {
        yield return $"{WorldB}?garbage";
        yield return $"{WorldB}~region(us)";
        yield return $"{WorldB}/12345";
        yield return $" {WorldB}";
        yield return $"{WorldB} ";
        yield return $"{WorldB}:";
        yield return $"{WorldB}:garbage";
        yield return $"{WorldB}:~region(us)";
        yield return $"{WorldB}:12345?garbage";
        yield return $"{WorldB}:12345/garbage";
        yield return $"{WorldB}:12345 region(us)";
        yield return $"{WorldB}:12345~~region(us)";
        yield return $"{WorldB}:12345~region()";
        yield return $"{WorldB}:12345~(us)";
        yield return $"{WorldB}:12345:other";
        yield return $"{WorldB}:12345#fragment";
    }

    private static void AssertCurrentWorldRequestSequence(TestFixture fixture, string worldId)
    {
        Assert.Collection(
            fixture.Handler.Requests,
            request => AssertRequest(request, "/api/1/auth/user"),
            request => AssertRequest(request, $"/api/1/worlds/{worldId}"));
        AssertNoFallbackRoutes(fixture.Handler.Requests);
    }

    private static void AssertOnlyWorldMetadataRequest(TestFixture fixture, string worldId)
    {
        AssertRequest(Assert.Single(fixture.Handler.Requests), $"/api/1/worlds/{worldId}");
        AssertNoFallbackRoutes(fixture.Handler.Requests);
    }

    private static void AssertOnlyAuthUserRequest(TestFixture fixture)
    {
        AssertRequest(Assert.Single(fixture.Handler.Requests), "/api/1/auth/user");
        AssertNoFallbackRoutes(fixture.Handler.Requests);
    }

    private static void AssertRequest(RecordedRequest request, string expectedPath)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(expectedPath, request.Uri.AbsolutePath);
        Assert.Equal(string.Empty, request.Uri.Query);
    }

    private static void AssertNoFallbackRoutes(IEnumerable<RecordedRequest> requests)
    {
        Assert.DoesNotContain(requests, request =>
            request.Uri.AbsolutePath.Contains("/instances", StringComparison.OrdinalIgnoreCase)
            || request.Uri.AbsolutePath.Contains("/worlds/active", StringComparison.OrdinalIgnoreCase)
            || request.Uri.Query.Contains("search", StringComparison.OrdinalIgnoreCase));
    }

    private static TestFixture CreateFixture(params string[] jsonResponses) =>
        new(jsonResponses.Select(JsonResponse).ToArray());

    private static TestFixture CreateFixture(params HttpResponseMessage[] responses) =>
        new(responses.Select<HttpResponseMessage, ResponseFactory>(response => (_, _) => Task.FromResult(response)).ToArray());

    private static TestFixture CreateFixture(params ResponseFactory[] responses) => new(responses);

    private static string CurrentUserJson(
        string? location = null,
        string? presenceLocation = null,
        string? presenceWorld = null,
        string? worldId = null,
        string? travelingToWorld = null,
        string? travelingToLocation = null,
        string? presenceTravelingToWorld = null) =>
        JsonSerializer.Serialize(new
        {
            id = "usr_99999999-9999-9999-9999-999999999999",
            location,
            worldId,
            travelingToWorld,
            travelingToLocation,
            presence = new
            {
                location = presenceLocation,
                world = presenceWorld,
                travelingToWorld = presenceTravelingToWorld
            }
        });

    private static string WorldJson(
        string id,
        string authorId,
        string name,
        string releaseStatus = "public",
        int? occupants = null,
        bool includeEmptyInstances = false) =>
        JsonSerializer.Serialize(new
        {
            id,
            authorId,
            name,
            releaseStatus,
            occupants,
            instances = includeEmptyInstances ? Array.Empty<object>() : null
        });

    private static ResponseFactory JsonResponse(string json) =>
        (_, _) => Task.FromResult(Response(HttpStatusCode.OK, json));

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private delegate Task<HttpResponseMessage> ResponseFactory(
        HttpRequestMessage request,
        CancellationToken cancellationToken);

    private sealed class TestFixture : IDisposable
    {
        public TestFixture(params ResponseFactory[] responses)
        {
            Handler = new QueueHttpMessageHandler(responses);
            HttpClient = new HttpClient(Handler);
            Service = new VrChatApiClient(HttpClient);
        }

        public QueueHttpMessageHandler Handler { get; }

        public HttpClient HttpClient { get; }

        public VrChatApiClient Service { get; }

        public void Dispose()
        {
            Service.Dispose();
            HttpClient.Dispose();
        }
    }

    private sealed class QueueHttpMessageHandler(params ResponseFactory[] responses) : HttpMessageHandler
    {
        private readonly Queue<ResponseFactory> responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        public void Enqueue(string json) => responses.Enqueue(JsonResponse(json));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => (IReadOnlyList<string>)header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase)));

            if (responses.Count == 0)
            {
                throw new UnexpectedRequestException($"No synthetic response was queued for {request.RequestUri}.");
            }

            return responses.Dequeue()(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Headers);

    private sealed class UnexpectedRequestException(string message) : Exception(message);
}
