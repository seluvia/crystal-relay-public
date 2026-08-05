using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatLoginCookieRegressionTests
{
    private const string SyntheticAuthCookie = "synthetic-login-cookie";

    [Fact]
    public async Task NonTwoFactorLogin_PreservesCookieForAccountAndLaterAuthenticatedRequest()
    {
        using var fixture = CreateFixture(
            Response(HttpStatusCode.OK, CurrentUserJson(), SyntheticAuthCookie),
            Response(HttpStatusCode.OK, CurrentUserJson()));

        var login = await fixture.Service.LoginWithCredentialsAsync("synthetic-user", "synthetic-password");

        Assert.Equal(SyntheticAuthCookie, login.AuthCookie);
        Assert.NotNull(login.Account);
        Assert.Equal(SyntheticAuthCookie, login.Account!.AuthCookie);

        await fixture.Service.GetCurrentUserAsync(login.Account.AuthCookie);

        Assert.Equal(
            $"auth={SyntheticAuthCookie}",
            Assert.Single(fixture.Handler.Requests[1].Headers["Cookie"]));
    }

    [Fact]
    public async Task TwoFactorRequiredOnHttp200_PreservesCookieAndAvailableMethods()
    {
        using var fixture = CreateFixture(
            Response(HttpStatusCode.OK, TwoFactorRequiredJson(), SyntheticAuthCookie));

        var login = await fixture.Service.LoginWithCredentialsAsync("synthetic-user", "synthetic-password");

        Assert.Equal(SyntheticAuthCookie, login.AuthCookie);
        Assert.Null(login.Account);
        Assert.Equal(
            new[]
            {
                VrChatTwoFactorMethod.Totp,
                VrChatTwoFactorMethod.EmailOtp,
                VrChatTwoFactorMethod.RecoveryCode
            },
            login.RequiredTwoFactorMethods);
    }

    [Fact]
    public async Task TwoFactorRequiredOnHttp401_PreservesCookieAndAvailableMethods()
    {
        using var fixture = CreateFixture(
            Response(HttpStatusCode.Unauthorized, TwoFactorRequiredErrorJson(), SyntheticAuthCookie));

        var login = await fixture.Service.LoginWithCredentialsAsync("synthetic-user", "synthetic-password");

        Assert.Equal(SyntheticAuthCookie, login.AuthCookie);
        Assert.Null(login.Account);
        Assert.Equal(3, login.RequiredTwoFactorMethods.Count);
        Assert.Contains(VrChatTwoFactorMethod.Totp, login.RequiredTwoFactorMethods);
        Assert.Contains(VrChatTwoFactorMethod.EmailOtp, login.RequiredTwoFactorMethods);
        Assert.Contains(VrChatTwoFactorMethod.RecoveryCode, login.RequiredTwoFactorMethods);
    }

    [Theory]
    [InlineData(VrChatTwoFactorMethod.Totp, "/api/1/auth/twofactorauth/totp/verify")]
    [InlineData(VrChatTwoFactorMethod.EmailOtp, "/api/1/auth/twofactorauth/emailotp/verify")]
    [InlineData(VrChatTwoFactorMethod.RecoveryCode, "/api/1/auth/twofactorauth/otp/verify")]
    public async Task SuccessfulTwoFactorCompletion_PreservesCookieOnVerificationAndCurrentUserRequests(
        VrChatTwoFactorMethod method,
        string verificationPath)
    {
        using var fixture = CreateFixture(
            Response(HttpStatusCode.OK, TwoFactorRequiredJson(), SyntheticAuthCookie),
            Response(HttpStatusCode.OK, "{}"),
            Response(HttpStatusCode.OK, CurrentUserJson()));

        var login = await fixture.Service.LoginWithCredentialsAsync("synthetic-user", "synthetic-password");
        var account = await fixture.Service.CompleteTwoFactorAsync(
            login.AuthCookie,
            method,
            " 123456 ");

        Assert.Equal(SyntheticAuthCookie, account.AuthCookie);
        Assert.Collection(
            fixture.Handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/api/1/auth/user", request.Uri.AbsolutePath);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal(verificationPath, request.Uri.AbsolutePath);
                Assert.Equal("{\"code\":\"123456\"}", request.Body);
                AssertAuthCookie(request);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/api/1/auth/user", request.Uri.AbsolutePath);
                AssertAuthCookie(request);
            });
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    public async Task Login_RejectsResponsesWithoutReusableCookie(
        HttpStatusCode statusCode,
        bool requiresTwoFactor)
    {
        using var fixture = CreateFixture(Response(
            statusCode,
            requiresTwoFactor ? TwoFactorRequiredJson() : CurrentUserJson()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.LoginWithCredentialsAsync("synthetic-user", "synthetic-password"));

        Assert.Contains("reusable auth session", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TestFixture CreateFixture(params HttpResponseMessage[] responses) => new(responses);

    private static string CurrentUserJson() => JsonSerializer.Serialize(new
    {
        id = "usr_99999999-9999-9999-9999-999999999999",
        displayName = "Synthetic VRChat User",
        currentAvatar = "avtr_99999999-9999-9999-9999-999999999999"
    });

    private static string TwoFactorRequiredJson() => JsonSerializer.Serialize(new
    {
        requiresTwoFactorAuth = new[] { "totp", "emailotp", "otp" }
    });

    private static string TwoFactorRequiredErrorJson() => JsonSerializer.Serialize(new
    {
        error = new
        {
            message = "Two-factor authentication required."
        }
    });

    private static void AssertAuthCookie(RecordedRequest request) =>
        Assert.Equal(
            $"auth={SyntheticAuthCookie}",
            Assert.Single(request.Headers["Cookie"]));

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string json,
        string? authCookie = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (authCookie is not null)
        {
            response.Headers.Add("Set-Cookie", $"auth={authCookie}; Path=/; HttpOnly");
        }

        return response;
    }

    private sealed class TestFixture : IDisposable
    {
        public TestFixture(IReadOnlyCollection<HttpResponseMessage> responses)
        {
            Handler = new QueueHttpMessageHandler(responses);
            HttpClient = new HttpClient(Handler);
            Service = new VrChatApiClient(HttpClient);
        }

        public QueueHttpMessageHandler Handler { get; }

        private HttpClient HttpClient { get; }

        public VrChatApiClient Service { get; }

        public void Dispose()
        {
            Service.Dispose();
            HttpClient.Dispose();
        }
    }

    private sealed class QueueHttpMessageHandler(
        IReadOnlyCollection<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

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
                body));

            if (responses.Count == 0)
            {
                throw new InvalidOperationException($"No synthetic response was queued for {request.RequestUri}.");
            }

            return responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
        string? Body);
}
