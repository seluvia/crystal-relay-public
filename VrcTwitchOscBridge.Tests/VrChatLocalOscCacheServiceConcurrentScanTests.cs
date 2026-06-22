using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatLocalOscCacheServiceConcurrentScanTests : IDisposable
{
    private readonly string _root;

    public VrChatLocalOscCacheServiceConcurrentScanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CrystalRelay_ConcurrentScan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task LoadKnownAvatarsInRoot_ConcurrentCalls_DoNotCorruptInternalDictionary()
    {
        const string userId = "usr_test";
        var avatarFolder = Path.Combine(_root, "OSC", userId, "Avatars");
        Directory.CreateDirectory(avatarFolder);
        var avatarId = "avtr_test_concurrent";
        var filePath = Path.Combine(avatarFolder, avatarId + ".json");
        File.WriteAllText(filePath, BuildAvatarJson(avatarId));

        var service = new VrChatLocalOscCacheService();

        const int parallelCalls = 16;
        using var start = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, parallelCalls)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();
                return await service.LoadKnownAvatarsInRootAsync(
                    _root, userId, CancellationToken.None);
            }))
            .ToArray();

        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, list => Assert.Single(list));
    }

    private static string BuildAvatarJson(string avatarId) =>
        JsonSerializer.Serialize(new
        {
            id = avatarId,
            name = "Test Avatar"
        });
}
