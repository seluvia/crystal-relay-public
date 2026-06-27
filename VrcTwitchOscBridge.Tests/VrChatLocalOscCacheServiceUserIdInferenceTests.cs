using System;
using System.IO;
using System.Threading.Tasks;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatLocalOscCacheServiceUserIdInferenceTests : IDisposable
{
    private readonly string tempRoot;

    public VrChatLocalOscCacheServiceUserIdInferenceTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "cr-userid-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private string CreateOscUser(string userId, DateTime lastWriteTimeUtc)
    {
        var dir = Path.Combine(tempRoot, "OSC", userId, "Avatars");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".touch"), "x");
        Directory.SetLastWriteTimeUtc(dir, lastWriteTimeUtc);
        return userId;
    }

    private string CreateOscAvatarFile(string userId, string avatarId, string avatarName, DateTime lastWriteTimeUtc)
    {
        var dir = Path.Combine(tempRoot, "OSC", userId, "Avatars");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, avatarId + ".json");
        File.WriteAllText(filePath, $"{{\"id\":\"{avatarId}\",\"name\":\"{avatarName}\",\"parameters\":[]}}");
        File.SetLastWriteTimeUtc(filePath, lastWriteTimeUtc);
        Directory.SetLastWriteTimeUtc(Path.Combine(tempRoot, "OSC", userId), lastWriteTimeUtc);
        return filePath;
    }

    [Fact]
    public void TryInferUserIdFromLocalLowInRoot_WhenNoOscFolder_ReturnsNull()
    {
        var result = VrChatLocalOscCacheService.TryInferUserIdFromLocalLowInRoot(tempRoot);
        Assert.Null(result);
    }

    [Fact]
    public void TryInferUserIdFromLocalLowInRoot_WhenSingleUserFolder_ReturnsThatUserId()
    {
        var expected = CreateOscUser("usr_single", DateTime.UtcNow);
        var result = VrChatLocalOscCacheService.TryInferUserIdFromLocalLowInRoot(tempRoot);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryInferUserIdFromLocalLowInRoot_WhenMultipleUserFolders_ReturnsMostRecentlyModified()
    {
        var older = DateTime.UtcNow.AddDays(-2);
        var newer = DateTime.UtcNow.AddDays(-1);
        CreateOscUser("usr_older", older);
        var expected = CreateOscUser("usr_newer", newer);
        var result = VrChatLocalOscCacheService.TryInferUserIdFromLocalLowInRoot(tempRoot);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task LoadLatestKnownAvatarInRootAsync_WhenMultipleAvatarFiles_ReturnsNewestAvatar()
    {
        var older = DateTime.UtcNow.AddMinutes(-10);
        var newer = DateTime.UtcNow.AddMinutes(-1);
        CreateOscAvatarFile("usr_cached", "avtr_old", "Old Avatar", older);
        CreateOscAvatarFile("usr_cached", "avtr_new", "New Avatar", newer);

        var service = new VrChatLocalOscCacheService();
        var latest = await service.LoadLatestKnownAvatarInRootAsync(tempRoot, "usr_cached");

        Assert.NotNull(latest);
        Assert.Equal("avtr_new", latest.AvatarId);
        Assert.Equal("New Avatar", latest.AvatarName);
    }
}
