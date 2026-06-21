using System.Collections.Generic;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class OscAvatarChangeMergerTests
{
    [Fact]
    public void MergeIntoList_EmptyList_AddsNewEntry()
    {
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing: new List<VrChatAvatarSummary>(),
            avatarId: "avtr_abc",
            resolvedName: "My Avatar",
            newSourceLabel: "Local OSC");
        Assert.Single(result);
        Assert.Equal("avtr_abc", result[0].Id);
        Assert.Equal("My Avatar", result[0].Name);
        Assert.Equal("Local OSC", result[0].SourceLabel);
    }

    [Fact]
    public void MergeIntoList_NewIdWithBlankName_FallsBackToId()
    {
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing: new List<VrChatAvatarSummary>(),
            avatarId: "avtr_abc",
            resolvedName: "",
            newSourceLabel: "Local OSC");
        Assert.Equal("avtr_abc", result[0].Name);
    }

    [Fact]
    public void MergeIntoList_ExistingEntryWithIdAsName_AdoptsBetterName()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            new("avtr_abc", "avtr_abc", "Local OSC", false, null),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "avtr_abc", "Real Name", "Local OSC");
        Assert.Single(result);
        Assert.Equal("Real Name", result[0].Name);
    }

    [Fact]
    public void MergeIntoList_ExistingEntryWithBetterName_PreservesIt()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            new("avtr_abc", "Real Name", "Uploaded", false, null),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "avtr_abc", "Other Name", "Local OSC");
        Assert.Single(result);
        Assert.Equal("Real Name", result[0].Name);
        Assert.Equal("Uploaded", result[0].SourceLabel);
    }

    [Fact]
    public void MergeIntoList_EmptyAvatarId_ReturnsUnchanged()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            new("avtr_abc", "Real Name", "Uploaded", false, null),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "", "X", "Local OSC");
        Assert.Equal(existing, result);
    }

    [Fact]
    public void MergeIntoList_MalformedId_ReturnsUnchanged()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            new("avtr_abc", "Real Name", "Uploaded", false, null),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "not_an_avatar_id", "X", "Local OSC");
        Assert.Equal(existing, result);
    }

    [Fact]
    public void MergeIntoList_ExistingEntry_PreservesIsCurrentAvatarAndThumbnail()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            new("avtr_abc", "avtr_abc", "Local OSC", IsCurrentAvatar: true, ThumbnailUrl: "https://example.test/thumb.png"),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "avtr_abc", "Real Name", "Local OSC");
        Assert.Single(result);
        Assert.True(result[0].IsCurrentAvatar);
        Assert.Equal("https://example.test/thumb.png", result[0].ThumbnailUrl);
    }
}
