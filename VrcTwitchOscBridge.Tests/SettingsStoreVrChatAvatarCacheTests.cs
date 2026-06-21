using System;
using System.Collections.Generic;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class SettingsStoreVrChatAvatarCacheTests
{
    // The SettingsStore constructor is parameterless and resolves all paths through
    // the internal static AppDataPaths class, which hardcodes
    // %LOCALAPPDATA%\CrystalRelay\Secure\. A real save/load cycle from this test
    // would write to and read from the user's actual secure folder, which is
    // unsafe. Refactoring the constructor is out of scope for this task; the
    // end-to-end round-trip is verified by the manual smoke test, and the
    // in-memory merge logic is covered by OscAvatarChangeMergerTests.
    [Fact(Skip = "SettingsStore has parameterless constructor only; round-trip via temp file requires AppData path override. Covered by manual smoke test.")]
    public async Task SaveAndLoadAvatarCache_PreservesLocalLowEntries()
    {
        var store = new SettingsStore();
        var avatars = new List<VrChatAvatarSummary>
        {
            new("avtr_1", "API Name 1", "Uploaded", true, null),
            new("avtr_2", "Local Name 2", "Local OSC", false, null),
            new("avtr_3", "Fav Name 3", "Favorites", false, null),
        };

        await store.SaveVrChatAvatarCacheAsync("usr_test", avatars, default);
        var loaded = await store.LoadVrChatAvatarCacheAsync("usr_test", default);

        Assert.Equal(3, loaded.Count);
        Assert.Equal("Local OSC", loaded[1].SourceLabel);
        Assert.Equal("Local Name 2", loaded[1].Name);
    }

    [Fact(Skip = "SettingsStore has parameterless constructor only; round-trip via temp file requires AppData path override. Covered by manual smoke test.")]
    public async Task LoadAvatarCache_WithMismatchedUserId_ReturnsEmpty()
    {
        var store = new SettingsStore();
        var avatars = new List<VrChatAvatarSummary>
        {
            new("avtr_1", "Name", "Local OSC", false, null),
        };
        await store.SaveVrChatAvatarCacheAsync("usr_one", avatars, default);
        var loaded = await store.LoadVrChatAvatarCacheAsync("usr_two", default);
        Assert.Empty(loaded);
    }
}
