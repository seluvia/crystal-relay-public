using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatAvatarAccessRegressionTests
{
    private const string CurrentAvatarId = "avtr_11111111-1111-1111-1111-111111111111";
    private const string ReturnedAvatarId = "avtr_22222222-2222-2222-2222-222222222222";
    private const string PcQuestAvatarId = "avtr_33333333-3333-3333-3333-333333333333";
    private const string QuestObjectAvatarId = "avtr_44444444-4444-4444-4444-444444444444";

    [Fact]
    public async Task GetSelectableAvatarsAsync_IgnoresLegacyScalarUnityPackagesAndReturnsAvatar()
    {
        using var fixture = CreateFixture();

        var avatars = await fixture.Service.GetSelectableAvatarsAsync(
            "synthetic-auth-cookie",
            CurrentAvatarId);

        Assert.Equal(3, avatars.Count);

        var legacyAvatar = Assert.Single(avatars, avatar => avatar.Id == ReturnedAvatarId);
        Assert.Equal("Legacy Avatar", legacyAvatar.Name);
        Assert.Equal(string.Empty, legacyAvatar.Platform);

        var pcQuestAvatar = Assert.Single(avatars, avatar => avatar.Id == PcQuestAvatarId);
        Assert.Equal("Both", pcQuestAvatar.Platform);

        var questObjectAvatar = Assert.Single(avatars, avatar => avatar.Id == QuestObjectAvatarId);
        Assert.Equal("Quest", questObjectAvatar.Platform);
    }

    private static TestFixture CreateFixture() => new();

    private sealed class TestFixture : IDisposable
    {
        public TestFixture()
        {
            Handler = new AvatarResponseHandler();
            HttpClient = new HttpClient(Handler);
            Service = new VrChatApiClient(HttpClient);
        }

        public AvatarResponseHandler Handler { get; }

        private HttpClient HttpClient { get; }

        public VrChatApiClient Service { get; }

        public void Dispose()
        {
            Service.Dispose();
            HttpClient.Dispose();
        }
    }

    private sealed class AvatarResponseHandler : HttpMessageHandler
    {
        private readonly string uploadedAvatars = JsonSerializer.Serialize(new object[]
        {
            new
            {
                id = ReturnedAvatarId,
                name = "Legacy Avatar",
                unityPackages = "legacy-package-metadata"
            },
            new
            {
                id = PcQuestAvatarId,
                name = "PC + Quest Avatar",
                unityPackages = new[]
                {
                    new { platform = "standalonewindows" },
                    new { platform = "android" }
                }
            },
            new
            {
                id = QuestObjectAvatarId,
                name = "Quest Object Avatar",
                unityPackages = new { platform = "android" }
            }
        });

        public ConcurrentBag<Uri> RequestUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/api/1/avatars" => uploadedAvatars,
                "/api/1/avatars/favorites" => "[]",
                "/api/1/avatars/licensed" => "[]",
                _ => throw new InvalidOperationException($"Unexpected synthetic request: {request.RequestUri}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
