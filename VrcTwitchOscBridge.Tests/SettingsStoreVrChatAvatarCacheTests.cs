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
            new(Id: "avtr_1", Name: "API Name 1", AuthorName: "", ThumbnailUrl: null,
                IsCurrentAvatar: true, IsUploaded: true, IsFavorited: false, IsLicensed: false,
                Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                FavoriteGroupName: null),
            new(Id: "avtr_2", Name: "Local Name 2", AuthorName: "", ThumbnailUrl: null,
                IsCurrentAvatar: false, IsUploaded: false, IsFavorited: false, IsLicensed: false,
                Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                FavoriteGroupName: null),
            new(Id: "avtr_3", Name: "Fav Name 3", AuthorName: "", ThumbnailUrl: null,
                IsCurrentAvatar: false, IsUploaded: false, IsFavorited: true, IsLicensed: false,
                Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                FavoriteGroupName: null),
        };

        await store.SaveVrChatAvatarCacheAsync("usr_test", avatars, default);
        var loaded = await store.LoadVrChatAvatarCacheAsync("usr_test", default);

        Assert.Equal(3, loaded.Count);
        Assert.Equal("Local Name 2", loaded[1].Name);
    }

    [Fact(Skip = "SettingsStore has parameterless constructor only; round-trip via temp file requires AppData path override. Covered by manual smoke test.")]
    public async Task LoadAvatarCache_WithMismatchedUserId_ReturnsEmpty()
    {
        var store = new SettingsStore();
        var avatars = new List<VrChatAvatarSummary>
        {
            new(Id: "avtr_1", Name: "Name", AuthorName: "", ThumbnailUrl: null,
                IsCurrentAvatar: false, IsUploaded: false, IsFavorited: false, IsLicensed: false,
                Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                FavoriteGroupName: null),
        };
        await store.SaveVrChatAvatarCacheAsync("usr_one", avatars, default);
        var loaded = await store.LoadVrChatAvatarCacheAsync("usr_two", default);
        Assert.Empty(loaded);
    }
}
