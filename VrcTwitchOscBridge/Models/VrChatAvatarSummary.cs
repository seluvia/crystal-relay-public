namespace VrcTwitchOscBridge.Models;

public sealed record VrChatAvatarSummary(
    string Id,
    string Name,
    string AuthorName,
    string? ThumbnailUrl,
    bool IsCurrentAvatar,
    bool IsUploaded,
    bool IsFavorited,
    bool IsLicensed,
    string Platform,
    IReadOnlyList<string> StyleTags,
    IReadOnlyList<string> ContentTags,
    string? FavoriteGroupName)
{
    public string SourceLabel
    {
        get
        {
            var sources = new List<string>(3);
            if (IsUploaded) sources.Add("Uploaded");
            if (IsFavorited) sources.Add("Favorites");
            if (IsLicensed) sources.Add("Licensed");
            return string.Join(" / ", sources);
        }
    }
}
