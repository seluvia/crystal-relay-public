using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatAccountSettingsCachedModeTests
{
    [Fact]
    public void Clear_RemovesAuthCookieButKeepsCachedIdentityForOfflineMode()
    {
        var settings = new VrChatAccountSettings
        {
            AuthCookie = "auth-cookie",
            UserId = "usr_cached",
            DisplayName = "Cached User",
            CurrentAvatarId = "avtr_current"
        };

        settings.Clear();

        Assert.Empty(settings.AuthCookie);
        Assert.Equal("usr_cached", settings.UserId);
        Assert.Equal("Cached User", settings.DisplayName);
        Assert.Equal("avtr_current", settings.CurrentAvatarId);
        Assert.False(settings.IsConnected);
    }

    [Fact]
    public void CreateVrChatAccountSettingsForLoad_WithMetadataAndNoAuthCookie_LoadsCachedIdentityDisconnected()
    {
        var settings = SettingsStore.CreateVrChatAccountSettingsForLoad(
            authCookie: string.Empty,
            userId: "usr_cached",
            displayName: "Cached User",
            currentAvatarId: "avtr_current");

        Assert.Empty(settings.AuthCookie);
        Assert.Equal("usr_cached", settings.UserId);
        Assert.Equal("Cached User", settings.DisplayName);
        Assert.Equal("avtr_current", settings.CurrentAvatarId);
        Assert.False(settings.IsConnected);
    }

    [Fact]
    public void SelectVrChatAvatarsForCachedModeAfterAuthFailure_KeepsFullCachedAvatarList()
    {
        var avatars = new[]
        {
            new VrChatAvatarSummary("avtr_uploaded", "Uploaded Avatar", "Uploaded", false, null),
            new VrChatAvatarSummary("avtr_local", "Local Avatar", "Local OSC", true, null),
            new VrChatAvatarSummary("avtr_favorite", "Favorite Avatar", "Favorites", false, null)
        };

        var kept = MainWindowViewModel.SelectVrChatAvatarsForCachedModeAfterAuthFailure(avatars);

        Assert.Equal(avatars, kept);
    }
}
