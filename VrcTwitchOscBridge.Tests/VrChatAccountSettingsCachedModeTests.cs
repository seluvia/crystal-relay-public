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
            new VrChatAvatarSummary(
                Id: "avtr_uploaded", Name: "Uploaded Avatar", AuthorName: "", ThumbnailUrl: null,
                IsCurrentAvatar: false, IsUploaded: true, IsFavorited: false, IsLicensed: false,
                Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                FavoriteGroupName: null),
            new VrChatAvatarSummary(
                Id: "avtr_local", Name: "Local Avatar", AuthorName: "", ThumbnailUrl: null,
                IsCurrentAvatar: true, IsUploaded: false, IsFavorited: false, IsLicensed: false,
                Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                FavoriteGroupName: null),
            new VrChatAvatarSummary(
                Id: "avtr_favorite", Name: "Favorite Avatar", AuthorName: "", ThumbnailUrl: null,
                IsCurrentAvatar: false, IsUploaded: false, IsFavorited: true, IsLicensed: false,
                Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
                FavoriteGroupName: null)
        };

        var kept = MainWindowViewModel.SelectVrChatAvatarsForCachedModeAfterAuthFailure(avatars);

        Assert.Equal(avatars, kept);
    }
}
