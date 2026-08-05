using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class WorldCommandBlacklistServiceTests
{
    private const string WorldId = "wrld_11111111-2222-3333-4444-555555555555";
    private const string AuthorId = "usr_aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    public static TheoryData<string> InvalidDecisionPayloads => new()
    {
        "{}",
        "null",
        "[]",
        "{",
        "{\"blocked\":false}",
        "{\"schemaVersion\":1,\"blocked\":false}",
        "{\"schemaVersion\":\"2\",\"blocked\":false}",
        "{\"schemaVersion\":null,\"blocked\":false}",
        "{\"schemaVersion\":2}",
        "{\"schemaVersion\":2,\"blocked\":\"false\"}",
        "{\"schemaVersion\":2,\"blocked\":null}",
        "{\"schemaVersion\":2,\"blocked\":false,\"reason\":\"must-not-cross-the-wire\"}",
        "{\"schemaVersion\":2,\"blocked\":false,\"extra\":true}"
    };

    public static TheoryData<string> DuplicateOrAliasedDecisionPayloads => new()
    {
        "{\"schemaVersion\":1,\"schemaVersion\":2,\"blocked\":false}",
        "{\"schemaVersion\":2,\"blocked\":true,\"blocked\":false}",
        "{\"SchemaVersion\":1,\"schemaVersion\":2,\"blocked\":false}",
        "{\"Blocked\":true,\"schemaVersion\":2,\"blocked\":false}"
    };

    public static TheoryData<string?, string?> InvalidMetadata => new()
    {
        { null, AuthorId },
        { "", AuthorId },
        { "   ", AuthorId },
        { $" {WorldId}", AuthorId },
        { $"{WorldId} ", AuthorId },
        { "wrld_not-a-uuid", AuthorId },
        { "wrld_11111111222233334444555555555555", AuthorId },
        { "usr_11111111-2222-3333-4444-555555555555", AuthorId },
        { WorldId, null },
        { WorldId, "" },
        { WorldId, "   " },
        { WorldId, $" {AuthorId}" },
        { WorldId, $"{AuthorId} " },
        { WorldId, "usr_not-a-uuid" },
        { WorldId, "usr_aaaaaaaabbbbccccddddeeeeeeeeeeee" },
        { WorldId, "wrld_aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }
    };

    public static TheoryData<string> InvalidStatusPayloads => new()
    {
        "{}",
        "null",
        "[]",
        "{",
        "{\"ok\":false,\"worldEntryCount\":1,\"creatorEntryCount\":1}",
        "{\"ok\":null,\"worldEntryCount\":1,\"creatorEntryCount\":1}",
        "{\"worldEntryCount\":1,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":null,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":null}",
        "{\"ok\":true,\"worldEntryCount\":\"1\",\"creatorEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":\"1\"}",
        "{\"ok\":true,\"worldEntryCount\":-1,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":-1}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"updatedAt\":null}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"updatedAt\":\"not-a-date\"}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"updatedAt\":\"2026-07-24T12:34:56\"}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"updatedAt\":\"2026-07-24T12:34:56+01:00\"}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"updatedAt\":\"2026-07-24T12:34:56-00:00\"}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"revision\":0}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"storageState\":\"bootstrap-legacy\"}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":0,\"storageState\":\"bootstrap-legacy\"}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":null,\"revision\":0,\"storageState\":\"bootstrap-legacy\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":1,\"revision\":0,\"storageState\":\"bootstrap-legacy\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":\"2\",\"revision\":0,\"storageState\":\"bootstrap-legacy\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":null,\"storageState\":\"bootstrap-legacy\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":\"0\",\"storageState\":\"bootstrap-legacy\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":0,\"storageState\":null,\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":0,\"storageState\":\"unknown\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":0,\"storageState\":\"bootstrap-legacy\",\"totalEntries\":null}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":0,\"storageState\":\"bootstrap-legacy\",\"totalEntries\":\"2\"}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":1,\"storageState\":\"bootstrap-legacy\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":1,\"storageState\":\"legacy-freeze\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":0,\"storageState\":\"verification-pending\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":0,\"storageState\":\"active\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":-1,\"storageState\":\"active\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":1,\"storageState\":\"active\",\"totalEntries\":-1}",
        "{\"ok\":true,\"worldEntryCount\":2,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":1,\"storageState\":\"active\",\"totalEntries\":2}"
    };

    public static TheoryData<string> DuplicateOrAliasedStatusPayloads => new()
    {
        "{\"ok\":false,\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":-1,\"worldEntryCount\":1,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":-1,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":1,\"schemaVersion\":2,\"revision\":1,\"storageState\":\"active\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":0,\"revision\":1,\"storageState\":\"active\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":1,\"storageState\":\"unknown\",\"storageState\":\"active\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":1,\"storageState\":\"active\",\"totalEntries\":1,\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"updatedAt\":\"2026-07-24T12:34:56+01:00\",\"updatedAt\":\"2026-07-24T12:34:56Z\"}",
        "{\"Ok\":false,\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"WorldEntryCount\":-1,\"worldEntryCount\":1,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":1,\"CreatorEntryCount\":-1,\"creatorEntryCount\":1}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"SchemaVersion\":1,\"schemaVersion\":2,\"revision\":1,\"storageState\":\"active\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"Revision\":0,\"revision\":1,\"storageState\":\"active\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":1,\"StorageState\":\"unknown\",\"storageState\":\"active\",\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"schemaVersion\":2,\"revision\":1,\"storageState\":\"active\",\"TotalEntries\":1,\"totalEntries\":2}",
        "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1,\"UpdatedAt\":\"2026-07-24T12:34:56+01:00\",\"updatedAt\":\"2026-07-24T12:34:56Z\"}"
    };

    public static TheoryData<string> InvalidUtcTimestamps => new()
    {
        "07/24/2026 12:34:56Z",
        "24 July 2026 12:34:56Z",
        "2026-07-24 12:34:56Z",
        "2026-7-24T12:34:56Z",
        "2026-07-24T12:34Z",
        "2026-07-24T12:34:56z",
        "2026-07-24T12:34:56+0000",
        "2026-07-24T12:34:56+00",
        "2026-07-24T12:34:56+01:00",
        "2026-07-24T12:34:56-00:00",
        "2026-07-24T12:34:56-01:00",
        "2026-07-24T12:34:56.12345678Z",
        "2026-02-29T12:34:56Z",
        "2026-07-24T12:34:56Z trailing"
    };

    [Fact]
    public void ParameterlessConstructor_RemainsPublic()
    {
        var constructor = typeof(WorldCommandBlacklistService).GetConstructor(Type.EmptyTypes);

        Assert.NotNull(constructor);
        Assert.True(constructor.IsPublic);
    }

    [Fact]
    public void InjectedConstructor_IsInternalAndConfiguresRequiredDefaultsWithoutDuplicates()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelay-DesktopApp");

        using var service = CreateInjectedService(httpClient);

        var constructor = GetInjectedConstructor();
        Assert.NotNull(constructor);
        Assert.True(constructor.IsAssembly);
        Assert.Equal(TimeSpan.FromSeconds(6), httpClient.Timeout);
        Assert.Single(httpClient.DefaultRequestHeaders.Accept, value =>
            string.Equals(value.MediaType, "application/json", StringComparison.OrdinalIgnoreCase));
        Assert.Single(httpClient.DefaultRequestHeaders.UserAgent, value =>
            string.Equals(value.Product?.Name, "CrystalRelay-DesktopApp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dispose_DefaultServiceDisposesItsOwnedClient()
    {
        var service = new WorldCommandBlacklistService();
        var httpClient = GetServiceClient(service);

        service.Dispose();

        Assert.Throws<ObjectDisposedException>(httpClient.CancelPendingRequests);
    }

    [Fact]
    public async Task Dispose_InjectedClientRemainsUsable()
    {
        using var handler = new RecordingHandler(JsonResponse("{}"));
        using var httpClient = new HttpClient(handler);
        var service = CreateInjectedService(httpClient);

        service.Dispose();

        Assert.False(handler.IsDisposed);
        using var response = await httpClient.GetAsync("https://example.invalid/still-owned-by-caller");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(handler.Requests);
        Assert.Equal(0, handler.RemainingResponseCount);
    }

    [Fact]
    public async Task WorkerDecisionFixture_ExactFalseAllows()
    {
        using var fixture = CreateFixture("{\"schemaVersion\":2,\"blocked\":false}");

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        Assert.False(decision.IsBlocked);
        Assert.False(decision.IsFailClosed);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task WorkerDecisionFixture_ExactTrueBlocksNormally()
    {
        using var fixture = CreateFixture("{\"schemaVersion\":2,\"blocked\":true}");

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        Assert.True(decision.IsBlocked);
        Assert.False(decision.IsFailClosed);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public void Decision_DoesNotExposeReason()
    {
        Assert.Null(typeof(WorldCommandBlacklistDecision).GetProperty("Reason"));
    }

    [Theory]
    [MemberData(nameof(InvalidDecisionPayloads))]
    public async Task InvalidDecisionPayload_FailsClosed(string payload)
    {
        using var fixture = CreateFixture(payload);

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        AssertFailClosed(decision);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [MemberData(nameof(DuplicateOrAliasedDecisionPayloads))]
    public async Task Decision_DuplicateOrCaseVariantKnownPropertyFailsClosed(string payload)
    {
        using var fixture = CreateFixture(payload);

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        AssertFailClosed(decision);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Decision_NonSuccessStatusFailsClosed(HttpStatusCode statusCode)
    {
        using var fixture = CreateFixture(JsonResponse("{}", statusCode));

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        AssertFailClosed(decision);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Decision_TimeoutFailsClosed()
    {
        using var fixture = CreateFixture((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Synthetic timeout.")));

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        AssertFailClosed(decision);
    }

    [Fact]
    public async Task Decision_InternalCancellationFailsClosed()
    {
        var internalCancellation = new CancellationToken(canceled: true);
        using var fixture = CreateFixture((_, _) =>
            Task.FromCanceled<HttpResponseMessage>(internalCancellation));

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        AssertFailClosed(decision);
    }

    [Fact]
    public async Task Decision_TransportFailureFailsClosed()
    {
        using var fixture = CreateFixture((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Synthetic transport failure.")));

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        AssertFailClosed(decision);
    }

    [Fact]
    public async Task Decision_CallerCancellationPropagates()
    {
        using var fixture = CreateNoRequestFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.EvaluateAsync(WorldId, AuthorId, cancellation.Token));
    }

    [Fact]
    public async Task Decision_CallerCancellationPreemptsMetadataValidation()
    {
        using var fixture = CreateNoRequestFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.EvaluateAsync(string.Empty, string.Empty, cancellation.Token));
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(CancellationRaceOutcome.NonSuccess)]
    [InlineData(CancellationRaceOutcome.Success)]
    [InlineData(CancellationRaceOutcome.MalformedJson)]
    [InlineData(CancellationRaceOutcome.TransportException)]
    public async Task Decision_CallerCancellationDuringSendIsAuthoritative(
        CancellationRaceOutcome outcome)
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = CreateFixture(CancellationRaceResponse(
            cancellation,
            outcome,
            "{\"schemaVersion\":2,\"blocked\":false}"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.EvaluateAsync(WorldId, AuthorId, cancellation.Token));
    }

    [Theory]
    [MemberData(nameof(InvalidMetadata))]
    public async Task InvalidMetadata_FailsClosedWithoutRequest(string? worldId, string? authorId)
    {
        using var fixture = CreateNoRequestFixture();

        var decision = await fixture.Service.EvaluateAsync(worldId!, authorId!);

        AssertFailClosed(decision);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task DecisionRequest_NormalizesIdsAndSendsOnlyThePublicEnvelope()
    {
        using var fixture = CreateFixture("{\"schemaVersion\":2,\"blocked\":false}");

        var decision = await fixture.Service.EvaluateAsync(
            WorldId.ToUpperInvariant(),
            AuthorId.ToUpperInvariant());

        Assert.False(decision.IsBlocked);
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://crystal-relay-world-guard.screminpal-animation.workers.dev/api/check",
            request.Uri.AbsoluteUri);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("application/json", Assert.Single(request.Headers["Accept"]));
        Assert.Equal("CrystalRelay-DesktopApp", Assert.Single(request.Headers["User-Agent"]));
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.False(request.Headers.ContainsKey("Cookie"));

        using var document = JsonDocument.Parse(request.Body!);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(
            new[] { "authorId", "worldId" },
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(WorldId, root.GetProperty("worldId").GetString());
        Assert.Equal(AuthorId, root.GetProperty("authorId").GetString());
    }

    [Fact]
    public async Task Decision_IsEnabledBeforeConfigure()
    {
        using var fixture = CreateFixture(
            JsonResponse("{\"schemaVersion\":2,\"blocked\":true}"),
            configure: false);

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        Assert.True(decision.IsBlocked);
        Assert.False(decision.IsFailClosed);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Decision_RemainsEnabledAfterConfigure()
    {
        using var fixture = CreateFixture(
            JsonResponse("{\"schemaVersion\":2,\"blocked\":true}"),
            configure: false);
        fixture.Service.Configure(new WorldCommandBlacklistSettings { IsEnabled = false });

        var decision = await fixture.Service.EvaluateAsync(WorldId, AuthorId);

        Assert.True(decision.IsBlocked);
        Assert.False(decision.IsFailClosed);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Status_OldShapeRemainsCompatible()
    {
        using var fixture = CreateFixture(
            "{\"ok\":true,\"worldEntryCount\":3,\"creatorEntryCount\":2}");

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.Ready, result.Status);
        Assert.Equal(3, result.WorldEntryCount);
        Assert.Equal(2, result.CreatorEntryCount);
        Assert.Null(result.UpdatedAtUtc);
    }

    [Fact]
    public async Task Status_OldShapeNormalizesUtcTimestamp()
    {
        using var fixture = CreateFixture(
            "{\"ok\":true,\"worldEntryCount\":3,\"creatorEntryCount\":2,"
            + "\"updatedAt\":\"2026-07-24T12:34:56+00:00\"}");

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.Ready, result.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-07-24T12:34:56Z"), result.UpdatedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.UpdatedAtUtc!.Value.Offset);
    }

    [Theory]
    [InlineData("2026-07-24T12:34:56Z", 0)]
    [InlineData("2026-07-24T12:34:56+00:00", 0)]
    [InlineData("2026-07-24T12:34:56.1Z", 1_000_000)]
    [InlineData("2026-07-24T12:34:56.1234567Z", 1_234_567)]
    [InlineData("2026-07-24T12:34:56.1+00:00", 1_000_000)]
    [InlineData("2026-07-24T12:34:56.1234567+00:00", 1_234_567)]
    public async Task Status_IsoUtcTimestampIsAcceptedAndNormalized(
        string timestamp,
        long fractionalTicks)
    {
        var payload = JsonSerializer.Serialize(new
        {
            ok = true,
            worldEntryCount = 3,
            creatorEntryCount = 2,
            updatedAt = timestamp
        });
        using var fixture = CreateFixture(payload);

        var result = await RefreshAsync(fixture.Service);

        var expected = new DateTimeOffset(2026, 7, 24, 12, 34, 56, TimeSpan.Zero)
            .AddTicks(fractionalTicks);
        Assert.Equal(WorldCommandBlacklistRefreshStatus.Ready, result.Status);
        Assert.Equal(expected, result.UpdatedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.UpdatedAtUtc!.Value.Offset);
    }

    [Theory]
    [MemberData(nameof(InvalidUtcTimestamps))]
    public async Task Status_NonIsoOrNonUtcTimestampReturnsInvalidResponse(string timestamp)
    {
        var payload = JsonSerializer.Serialize(new
        {
            ok = true,
            worldEntryCount = 1,
            creatorEntryCount = 1,
            updatedAt = timestamp
        });
        using var fixture = CreateFixture(payload);

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task WorkerBootstrapStatusFixture_RevisionZeroIsReadyAndUsesExactRoute()
    {
        const string payload = "{\"schemaVersion\":2,\"ok\":true,\"worldEntryCount\":3,"
            + "\"creatorEntryCount\":2,\"updatedAt\":\"2026-07-24T12:34:56.000Z\","
            + "\"revision\":0,\"storageState\":\"bootstrap-legacy\",\"totalEntries\":5}";
        using var fixture = CreateFixture(payload);

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.Ready, result.Status);
        Assert.Equal(3, result.WorldEntryCount);
        Assert.Equal(2, result.CreatorEntryCount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-24T12:34:56Z"), result.UpdatedAtUtc);
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://crystal-relay-world-guard.screminpal-animation.workers.dev/api/status",
            request.Uri.AbsoluteUri);
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task WorkerActiveStatusFixture_AuthoritativeRevisionIsReady()
    {
        const string payload = "{\"schemaVersion\":2,\"ok\":true,\"worldEntryCount\":4,"
            + "\"creatorEntryCount\":3,\"updatedAt\":\"2026-07-24T12:34:56.000Z\","
            + "\"revision\":7,\"storageState\":\"active\",\"totalEntries\":9}";
        using var fixture = CreateFixture(payload);

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.Ready, result.Status);
        Assert.Equal(4, result.WorldEntryCount);
        Assert.Equal(3, result.CreatorEntryCount);
    }

    [Theory]
    [InlineData("legacy-freeze", 0)]
    [InlineData("verification-pending", 1)]
    [InlineData("verification-pending", 99)]
    [InlineData("active", 1)]
    [InlineData("active", 99)]
    public async Task Status_ValidModeRevisionPairIsReady(string storageState, int revision)
    {
        var payload = "{\"schemaVersion\":2,\"ok\":true,\"worldEntryCount\":1,"
            + "\"creatorEntryCount\":1,\"revision\":" + revision
            + ",\"storageState\":\"" + storageState + "\",\"totalEntries\":2}";
        using var fixture = CreateFixture(payload);

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.Ready, result.Status);
    }

    [Theory]
    [MemberData(nameof(InvalidStatusPayloads))]
    public async Task Status_InvalidSuccessfulPayloadReturnsInvalidResponse(string payload)
    {
        using var fixture = CreateFixture(payload);

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.InvalidResponse, result.Status);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [MemberData(nameof(DuplicateOrAliasedStatusPayloads))]
    public async Task Status_DuplicateOrCaseVariantKnownPropertyReturnsInvalidResponse(string payload)
    {
        using var fixture = CreateFixture(payload);

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.InvalidResponse, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Status_NonSuccessReturnsRequestFailed(HttpStatusCode statusCode)
    {
        using var fixture = CreateFixture(JsonResponse("{}", statusCode));

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.RequestFailed, result.Status);
    }

    [Fact]
    public async Task Status_TimeoutReturnsRequestFailed()
    {
        using var fixture = CreateFixture((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Synthetic timeout.")));

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.RequestFailed, result.Status);
    }

    [Fact]
    public async Task Status_InternalCancellationReturnsRequestFailed()
    {
        var internalCancellation = new CancellationToken(canceled: true);
        using var fixture = CreateFixture((_, _) =>
            Task.FromCanceled<HttpResponseMessage>(internalCancellation));

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.RequestFailed, result.Status);
    }

    [Fact]
    public async Task Status_TransportFailureReturnsRequestFailed()
    {
        using var fixture = CreateFixture((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Synthetic transport failure.")));

        var result = await RefreshAsync(fixture.Service);

        Assert.Equal(WorldCommandBlacklistRefreshStatus.RequestFailed, result.Status);
    }

    [Fact]
    public async Task Status_CallerCancellationPropagates()
    {
        using var fixture = CreateNoRequestFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.RefreshAsync(
                new WorldCommandBlacklistSettings(),
                force: false,
                cancellation.Token));
    }

    [Theory]
    [InlineData(CancellationRaceOutcome.NonSuccess)]
    [InlineData(CancellationRaceOutcome.Success)]
    [InlineData(CancellationRaceOutcome.MalformedJson)]
    [InlineData(CancellationRaceOutcome.TransportException)]
    public async Task Status_CallerCancellationDuringSendIsAuthoritative(
        CancellationRaceOutcome outcome)
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = CreateFixture(CancellationRaceResponse(
            cancellation,
            outcome,
            "{\"ok\":true,\"worldEntryCount\":1,\"creatorEntryCount\":1}"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.RefreshAsync(
                new WorldCommandBlacklistSettings(),
                force: false,
                cancellation.Token));
    }

    private static async Task<WorldCommandBlacklistRefreshResult> RefreshAsync(
        WorldCommandBlacklistService service) =>
        await service.RefreshAsync(new WorldCommandBlacklistSettings(), force: false);

    private static void AssertFailClosed(WorldCommandBlacklistDecision decision)
    {
        Assert.True(decision.IsBlocked);
        Assert.True(decision.IsFailClosed);
    }

    private static TestFixture CreateFixture(string json, bool configure = true) =>
        CreateFixture(JsonResponse(json), configure);

    private static TestFixture CreateFixture(ResponseFactory response, bool configure = true) =>
        new(configure, 1, response);

    private static TestFixture CreateNoRequestFixture(bool configure = true) =>
        new(configure, expectedRequestCount: 0);

    private static ResponseFactory JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    private static ResponseFactory CancellationRaceResponse(
        CancellationTokenSource callerCancellation,
        CancellationRaceOutcome outcome,
        string successJson) =>
        (_, _) =>
        {
            callerCancellation.Cancel();
            return outcome switch
            {
                CancellationRaceOutcome.NonSuccess => JsonResponse(
                    "{}",
                    HttpStatusCode.ServiceUnavailable)(null!, default),
                CancellationRaceOutcome.Success => JsonResponse(successJson)(null!, default),
                CancellationRaceOutcome.MalformedJson => JsonResponse("{")(null!, default),
                CancellationRaceOutcome.TransportException =>
                    Task.FromException<HttpResponseMessage>(
                        new HttpRequestException("Synthetic transport failure after caller cancellation.")),
                _ => throw new ArgumentOutOfRangeException(nameof(outcome))
            };
        };

    private static ConstructorInfo? GetInjectedConstructor() =>
        typeof(WorldCommandBlacklistService).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(HttpClient)],
            modifiers: null);

    private static WorldCommandBlacklistService CreateInjectedService(HttpClient httpClient)
    {
        var constructor = GetInjectedConstructor();
        if (constructor is not null)
        {
            return (WorldCommandBlacklistService)constructor.Invoke([httpClient]);
        }

        // The pre-change service has no injection seam. Replacing its client lets the
        // intentional RED exercise existing wire behavior without touching a live API.
        var service = new WorldCommandBlacklistService();
        var field = GetHttpClientField();
        var ownedClient = (HttpClient)field.GetValue(service)!;
        field.SetValue(service, httpClient);
        ownedClient.Dispose();
        return service;
    }

    private static HttpClient GetServiceClient(WorldCommandBlacklistService service) =>
        (HttpClient)GetHttpClientField().GetValue(service)!;

    private static FieldInfo GetHttpClientField() =>
        typeof(WorldCommandBlacklistService).GetField(
            "httpClient",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

    public enum CancellationRaceOutcome
    {
        NonSuccess,
        Success,
        MalformedJson,
        TransportException
    }

    private delegate Task<HttpResponseMessage> ResponseFactory(
        HttpRequestMessage request,
        CancellationToken cancellationToken);

    private sealed class TestFixture : IDisposable
    {
        private readonly int expectedRequestCount;

        public TestFixture(
            bool configure,
            int expectedRequestCount,
            params ResponseFactory[] responses)
        {
            this.expectedRequestCount = expectedRequestCount;
            Handler = new RecordingHandler(responses);
            HttpClient = new HttpClient(Handler);
            Service = CreateInjectedService(HttpClient);
            if (configure)
            {
                Service.Configure(new WorldCommandBlacklistSettings());
            }
        }

        public RecordingHandler Handler { get; }

        public HttpClient HttpClient { get; }

        public WorldCommandBlacklistService Service { get; }

        public void Dispose()
        {
            try
            {
                Assert.Equal(expectedRequestCount, Handler.Requests.Count);
                Assert.Equal(0, Handler.RemainingResponseCount);
            }
            finally
            {
                Service.Dispose();
                HttpClient.Dispose();
            }
        }
    }

    private sealed class RecordingHandler(params ResponseFactory[] responses) : HttpMessageHandler
    {
        private readonly Queue<ResponseFactory> responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        public int RemainingResponseCount => responses.Count;

        public bool IsDisposed { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => (IReadOnlyList<string>)header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase),
                request.Content?.Headers.ContentType?.MediaType,
                body));

            if (responses.Count == 0)
            {
                throw new InvalidOperationException($"No synthetic response was queued for {request.RequestUri}.");
            }

            return await responses.Dequeue()(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
        string? ContentType,
        string? Body);
}
