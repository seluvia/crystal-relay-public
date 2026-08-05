using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ApplicationUpdateServiceTests
{
    [Fact]
    public async Task BugFix_ExactBase_ReturnsHighestSequenceWithReleaseNotes()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix1", ApplicationUpdateChannel.BugFix, body: "Fix one"),
            Release("v3.2.0-bugfix2", ApplicationUpdateChannel.BugFix, body: "Fix two\n- Full detail"));
        var build = Stable("3.2.0");

        var result = await service.CheckForUpdateAsync(build, "", "", false);

        var update = Assert.IsType<ApplicationUpdateInfo>(result.Update);
        Assert.Equal(ApplicationUpdateChannel.BugFix, update.Channel);
        Assert.True(update.IsBugFix);
        Assert.False(update.IsBeta);
        Assert.Equal(2, update.BugFixSequence);
        Assert.Equal("3.2.0", update.CurrentVersion);
        Assert.Equal("3.2.0-bugfix2", update.LatestVersion);
        Assert.Equal("3.2.0", update.LatestBaseVersion);
        Assert.Equal("Crystal Relay v3.2.0 Bug Fix Push 2", update.ReleaseTitle);
        Assert.Equal("Fix two\n- Full detail", update.ReleaseBody);
        Assert.Equal(
            "https://github.com/seluvia/crystal-relay-public/releases/tag/v3.2.0-bugfix2",
            update.ReleasePageUrl);
        Assert.Equal("CrystalRelayBugFix-v3.2.0-bugfix2-win-x64.zip", update.AssetName);
        Assert.Equal(
            "https://github.com/seluvia/crystal-relay-public/releases/download/v3.2.0-bugfix2/CrystalRelayBugFix-v3.2.0-bugfix2-win-x64.zip",
            update.AssetDownloadUrl);
        Assert.Equal(1024L, update.AssetSizeBytes);
        Assert.Equal($"sha256:{new string('a', 64)}", update.Sha256Digest);
    }

    [Fact]
    public async Task BugFix_DoesNotApplyToDifferentStableBase()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix1", ApplicationUpdateChannel.BugFix));

        var result = await service.CheckForUpdateAsync(Stable("3.1.9"), "", "", false);

        Assert.Equal(ApplicationUpdateCheckStatus.NoUpdate, result.Status);
    }

    [Fact]
    public async Task OlderClient_ReceivesNormalStableBeforeThatStableBugFix()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix2", ApplicationUpdateChannel.BugFix),
            Release("v3.2.0", ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(Stable("3.1.9"), "", "", false);

        Assert.Equal(ApplicationUpdateChannel.Stable, result.Update!.Channel);
        Assert.Equal("3.2.0", result.Update.LatestVersion);
    }

    [Fact]
    public async Task NewerStable_SupersedesMatchingBugFix()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix2", ApplicationUpdateChannel.BugFix),
            Release("v3.2.1", ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(Stable("3.2.0"), "", "", true);

        Assert.Equal(ApplicationUpdateChannel.Stable, result.Update!.Channel);
        Assert.Equal("3.2.1", result.Update.LatestVersion);
    }

    [Fact]
    public async Task IgnoredNewerStable_DoesNotSuppressMatchingBugFix()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix2", ApplicationUpdateChannel.BugFix),
            Release("v3.2.1", ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(
            Stable("3.2.0"),
            ignoredVersionText: "3.2.1",
            ignoredBetaBaseVersionText: string.Empty,
            includeBetaUpdates: false);

        Assert.Equal(ApplicationUpdateChannel.BugFix, result.Update!.Channel);
        Assert.Equal("3.2.0-bugfix2", result.Update.LatestVersion);
    }

    [Fact]
    public async Task BugFix_PrecedesOptionalBeta_AndIgnoresSkipSettings()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix1", ApplicationUpdateChannel.BugFix),
            Release("v3.2.1-beta1", ApplicationUpdateChannel.Beta));

        var result = await service.CheckForUpdateAsync(
            Stable("3.2.0"),
            ignoredVersionText: "3.2.0-bugfix1",
            ignoredBetaBaseVersionText: "3.2.1",
            includeBetaUpdates: true);

        Assert.Equal(ApplicationUpdateChannel.BugFix, result.Update!.Channel);
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Beta, false)]
    [InlineData(ApplicationUpdateChannel.Stable, true)]
    public async Task BugFix_TargetsOnlyStableOrBugFixBuilds(
        ApplicationUpdateChannel installedChannel,
        bool expectedUpdate)
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix1", ApplicationUpdateChannel.BugFix));
        var build = new ApplicationBuildIdentity(
            "3.2.0",
            installedChannel,
            installedChannel == ApplicationUpdateChannel.Beta ? "beta1" : string.Empty,
            installedChannel == ApplicationUpdateChannel.Beta ? "Beta 1" : string.Empty,
            BugFixSequence: 0,
            IsTestBuild: false);

        var result = await service.CheckForUpdateAsync(build, "", "", true);

        Assert.Equal(expectedUpdate, result.Update is not null);
    }

    [Fact]
    public async Task BugFix_DoesNotTargetTestBuild()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix1", ApplicationUpdateChannel.BugFix));
        var build = new ApplicationBuildIdentity(
            "3.2.0",
            ApplicationUpdateChannel.Stable,
            string.Empty,
            string.Empty,
            BugFixSequence: 0,
            IsTestBuild: true);

        var result = await service.CheckForUpdateAsync(build, "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task InstalledBugFix_DoesNotReofferSameSequenceOrBaseStable()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix1", ApplicationUpdateChannel.BugFix),
            Release("v3.2.0", ApplicationUpdateChannel.Stable));
        var build = new ApplicationBuildIdentity(
            "3.2.0",
            ApplicationUpdateChannel.BugFix,
            "bugfix1",
            "Bug Fix 1",
            BugFixSequence: 1,
            IsTestBuild: false);

        var result = await service.CheckForUpdateAsync(build, "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task ForcedCheck_DoesNotReofferBaseStableFromInstalledBugFix()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix1", ApplicationUpdateChannel.BugFix),
            Release("v3.2.0", ApplicationUpdateChannel.Stable));
        var build = new ApplicationBuildIdentity(
            "3.2.0",
            ApplicationUpdateChannel.BugFix,
            "bugfix1",
            "Bug Fix 1",
            BugFixSequence: 1,
            IsTestBuild: false);

        var result = await service.CheckForUpdateAlwaysAsync(build, "", "", false);

        Assert.Equal(ApplicationUpdateCheckStatus.NoUpdate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task InstalledBugFix_ReceivesOnlyGreaterCumulativeSequence()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix1", ApplicationUpdateChannel.BugFix),
            Release("v3.2.0-bugfix2", ApplicationUpdateChannel.BugFix),
            Release("v3.2.0-bugfix3", ApplicationUpdateChannel.BugFix));
        var build = new ApplicationBuildIdentity(
            "3.2.0",
            ApplicationUpdateChannel.BugFix,
            "bugfix2",
            "Bug Fix 2",
            BugFixSequence: 2,
            IsTestBuild: false);

        var result = await service.CheckForUpdateAsync(build, "", "", false);

        Assert.Equal("3.2.0-bugfix2", result.Update!.CurrentVersion);
        Assert.Equal("3.2.0-bugfix3", result.Update!.LatestVersion);
    }

    [Theory]
    [InlineData("v3.2.0-bugfix")]
    [InlineData("v3.2.0-bugfix0")]
    [InlineData("v3.2.0-bugfix-1")]
    [InlineData("3.2.0-bugfix1")]
    [InlineData("V3.2.0-bugfix1")]
    [InlineData("v3.2.0-BugFix1")]
    [InlineData("v3.2.0-bugfix1-extra")]
    [InlineData(" v3.2.0-bugfix1")]
    [InlineData("v3.2.0-bugfix1 ")]
    [InlineData("v3.2.0-hotfix1")]
    public async Task MalformedBugFixTags_AreIgnored(string tag)
    {
        using var service = CreateService(Release(tag, ApplicationUpdateChannel.BugFix));

        var result = await service.CheckForUpdateAsync(Stable("3.2.0"), "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task BugFix_WithStableAssetSubstitution_IsIgnored()
    {
        using var service = CreateService(Release(
            "v3.2.0-bugfix1",
            ApplicationUpdateChannel.BugFix,
            assetChannel: ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(Stable("3.2.0"), "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task BugFix_WithWrongAssetCasing_IsIgnored()
    {
        using var service = CreateService(Release(
            "v3.2.0-bugfix1",
            ApplicationUpdateChannel.BugFix,
            assetNameOverride: "crystalrelaybugfix-v3.2.0-bugfix1-win-x64.zip"));

        var result = await service.CheckForUpdateAsync(Stable("3.2.0"), "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task BugFix_MarkedAsPrerelease_IsIgnored()
    {
        using var service = CreateService(Release(
            "v3.2.0-bugfix1",
            ApplicationUpdateChannel.BugFix,
            prerelease: true));

        var result = await service.CheckForUpdateAsync(Stable("3.2.0"), "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task MalformedBugFix_DoesNotBlockValidStableCandidate()
    {
        using var service = CreateService(
            Release("v3.2.0-bugfix0", ApplicationUpdateChannel.BugFix),
            Release("v3.2.1", ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(Stable("3.2.0"), "", "", false);

        Assert.Equal(ApplicationUpdateChannel.Stable, result.Update!.Channel);
    }

    [Fact]
    public async Task StableSelection_RemainsAvailableWithoutBetaOptIn()
    {
        using var service = CreateService(
            Release("v3.2.0", ApplicationUpdateChannel.Stable),
            Release("v3.2.1-beta1", ApplicationUpdateChannel.Beta));

        var result = await service.CheckForUpdateAsync(Stable("3.1.9"), "", "", false);

        Assert.Equal(ApplicationUpdateChannel.Stable, result.Update!.Channel);
        Assert.Equal("3.2.0", result.Update.LatestVersion);
    }

    [Fact]
    public async Task BetaSelection_RemainsAvailableWhenEnabledAndNoStableExists()
    {
        using var service = CreateService(
            Release("v3.2.1-beta1", ApplicationUpdateChannel.Beta));

        var result = await service.CheckForUpdateAsync(Stable("3.2.0"), "", "", true);

        Assert.Equal(ApplicationUpdateChannel.Beta, result.Update!.Channel);
        Assert.Equal("3.2.1-beta1", result.Update.LatestVersion);
    }

    [Fact]
    public async Task Discovery_UsesOneRequestWithOneHundredReleasePageSize()
    {
        var handler = new ReleaseHandler([]);
        using var client = new HttpClient(handler);
        using var service = new ApplicationUpdateService(client);

        await service.CheckForUpdateAsync(Stable("3.2.0"), "", "", false);

        Assert.Single(handler.RequestUris);
        Assert.Contains("per_page=100", handler.RequestUris[0].Query, StringComparison.Ordinal);
    }

    private static ApplicationBuildIdentity Stable(string version) =>
        new(version, ApplicationUpdateChannel.Stable, string.Empty, string.Empty, 0, false);

    private static ApplicationUpdateService CreateService(params object[] releases)
    {
        var handler = new ReleaseHandler(releases);
        return new ApplicationUpdateService(new HttpClient(handler));
    }

    private static object Release(
        string tag,
        ApplicationUpdateChannel channel,
        string body = "Release details",
        ApplicationUpdateChannel? assetChannel = null,
        bool? prerelease = null,
        string? assetNameOverride = null)
    {
        var assetName = assetNameOverride
            ?? ApplicationUpdatePackageRules.GetExpectedAssetName(
                assetChannel ?? channel,
                tag.TrimStart('v', 'V'));
        return new
        {
            tag_name = tag,
            name = channel == ApplicationUpdateChannel.BugFix
                ? $"Crystal Relay v3.2.0 Bug Fix Push {ExtractSequence(tag)}"
                : tag,
            body,
            html_url = $"https://github.com/seluvia/crystal-relay-public/releases/tag/{tag}",
            draft = false,
            prerelease = prerelease ?? channel == ApplicationUpdateChannel.Beta,
            published_at = "2026-07-19T00:00:00Z",
            assets = new[]
            {
                new
                {
                    name = assetName,
                    browser_download_url = $"https://github.com/seluvia/crystal-relay-public/releases/download/{tag}/{assetName}",
                    size = 1024,
                    digest = $"sha256:{new string('a', 64)}"
                }
            }
        };
    }

    private static int ExtractSequence(string tag)
    {
        var marker = "bugfix";
        var index = tag.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 && int.TryParse(tag[(index + marker.Length)..], out var sequence)
            ? sequence
            : 0;
    }

    private sealed class ReleaseHandler(IEnumerable<object> releases) : HttpMessageHandler
    {
        private readonly string json = JsonSerializer.Serialize(releases);
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
