using System.Collections.Generic;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class OscAvatarChangeMergerTests
{
    private static VrChatAvatarSummary MakeAvatar(
        string id, string name, bool isCurrent = false, string? thumbnailUrl = null,
        bool isUploaded = false, bool isFavorited = false, bool isLicensed = false)
        => new(
            Id: id, Name: name, AuthorName: "", ThumbnailUrl: thumbnailUrl,
            IsCurrentAvatar: isCurrent,
            IsUploaded: isUploaded, IsFavorited: isFavorited, IsLicensed: isLicensed,
            Platform: "", StyleTags: Array.Empty<string>(), ContentTags: Array.Empty<string>(),
            FavoriteGroupName: null);

    [Fact]
    public void MergeIntoList_EmptyList_AddsNewEntry()
    {
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing: new List<VrChatAvatarSummary>(),
            avatarId: "avtr_abc",
            resolvedName: "My Avatar");
        Assert.Single(result);
        Assert.Equal("avtr_abc", result[0].Id);
        Assert.Equal("My Avatar", result[0].Name);
    }

    [Fact]
    public void MergeIntoList_NewIdWithBlankName_FallsBackToId()
    {
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing: new List<VrChatAvatarSummary>(),
            avatarId: "avtr_abc",
            resolvedName: "");
        Assert.Equal("avtr_abc", result[0].Name);
    }

    [Fact]
    public void MergeIntoList_ExistingEntryWithIdAsName_AdoptsBetterName()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            MakeAvatar("avtr_abc", "avtr_abc"),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "avtr_abc", "Real Name");
        Assert.Single(result);
        Assert.Equal("Real Name", result[0].Name);
    }

    [Fact]
    public void MergeIntoList_ExistingEntryWithBetterName_PreservesIt()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            MakeAvatar("avtr_abc", "Real Name", isUploaded: true),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "avtr_abc", "Other Name");
        Assert.Single(result);
        Assert.Equal("Real Name", result[0].Name);
        Assert.True(result[0].IsUploaded);
    }

    [Fact]
    public void MergeIntoList_EmptyAvatarId_ReturnsUnchanged()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            MakeAvatar("avtr_abc", "Real Name", isUploaded: true),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "", "X");
        Assert.Equal(existing, result);
    }

    [Fact]
    public void MergeIntoList_MalformedId_ReturnsUnchanged()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            MakeAvatar("avtr_abc", "Real Name", isUploaded: true),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "not_an_avatar_id", "X");
        Assert.Equal(existing, result);
    }

    [Fact]
    public void MergeIntoList_ExistingEntry_PreservesIsCurrentAvatarAndThumbnail()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            MakeAvatar("avtr_abc", "avtr_abc", isCurrent: true, thumbnailUrl: "https://example.test/thumb.png"),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "avtr_abc", "Real Name");
        Assert.Single(result);
        Assert.True(result[0].IsCurrentAvatar);
        Assert.Equal("https://example.test/thumb.png", result[0].ThumbnailUrl);
    }
}
