using System;
using System.Collections.Generic;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

internal static class OscAvatarChangeMerger
{
    public static IReadOnlyList<VrChatAvatarSummary> MergeIntoList(
        IReadOnlyList<VrChatAvatarSummary> existing,
        string avatarId,
        string resolvedName)
    {
        if (string.IsNullOrWhiteSpace(avatarId)) return existing;
        if (!avatarId.StartsWith("avtr_", StringComparison.Ordinal)) return existing;

        var result = new List<VrChatAvatarSummary>(existing);
        var idx = result.FindIndex(a => string.Equals(a.Id, avatarId, StringComparison.Ordinal));
        var finalName = string.IsNullOrWhiteSpace(resolvedName) ? avatarId : resolvedName;

        if (idx < 0)
        {
            result.Add(new VrChatAvatarSummary(
                Id: avatarId,
                Name: finalName,
                AuthorName: string.Empty,
                ThumbnailUrl: null,
                IsCurrentAvatar: false,
                IsUploaded: false,
                IsFavorited: false,
                IsLicensed: false,
                Platform: string.Empty,
                StyleTags: Array.Empty<string>(),
                ContentTags: Array.Empty<string>(),
                FavoriteGroupName: null));
        }
        else
        {
            var current = result[idx];
            var shouldAdopt = string.IsNullOrWhiteSpace(current.Name)
                || string.Equals(current.Name, current.Id, StringComparison.Ordinal);
            if (shouldAdopt || !string.Equals(current.Name, finalName, StringComparison.Ordinal))
            {
                result[idx] = current with
                {
                    Name = shouldAdopt ? finalName : current.Name,
                };
            }
        }
        return result;
    }
}
