using System.Collections.ObjectModel;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class AvatarLibrary : ObservableObject
{
    private AvatarPickerViewMode lastViewMode = AvatarPickerViewMode.Grid;
    private ObservableCollection<AvatarLibraryEntry> entries = [];
    private ObservableCollection<AvatarGroup> groups = [];
    private ObservableCollection<AvatarTag> tags = [];

    public AvatarPickerViewMode LastViewMode
    {
        get => lastViewMode;
        set => SetProperty(ref lastViewMode, value);
    }

    public ObservableCollection<AvatarLibraryEntry> Entries
    {
        get => entries;
        set => SetProperty(ref entries, value ?? []);
    }

    public ObservableCollection<AvatarGroup> Groups
    {
        get => groups;
        set => SetProperty(ref groups, value ?? []);
    }

    public ObservableCollection<AvatarTag> Tags
    {
        get => tags;
        set => SetProperty(ref tags, value ?? []);
    }

    private const int MaxRecentAvatars = 10;

    public List<string> RecentAvatarIds { get; set; } = [];

    public void TrackRecentAvatar(string avatarId)
    {
        RecentAvatarIds.Remove(avatarId);
        RecentAvatarIds.Insert(0, avatarId);
        if (RecentAvatarIds.Count > MaxRecentAvatars)
            RecentAvatarIds.RemoveRange(MaxRecentAvatars, RecentAvatarIds.Count - MaxRecentAvatars);
    }

    public AvatarLibraryEntry? GetEntry(string avatarId) =>
        Entries.FirstOrDefault(e => string.Equals(e.AvatarId, avatarId, StringComparison.Ordinal));

    public void EnsureEntry(string avatarId)
    {
        if (GetEntry(avatarId) is null)
        {
            Entries.Add(new AvatarLibraryEntry { AvatarId = avatarId });
        }
    }

    /// <summary>
    /// Removes any entry whose AvatarId is not in the current VRChat avatar list.
    /// Call when the picker opens with a fresh avatar list.
    /// </summary>
    public void PruneMissingEntries(IReadOnlyList<VrChatAvatarSummary> currentAvatars)
    {
        if (currentAvatars.Count == 0)
        {
            // Don't wipe the library on a transiently empty avatar list (failed fetch, still-loading).
            return;
        }

        var currentIds = new HashSet<string>(currentAvatars.Select(a => a.Id), StringComparer.Ordinal);
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (!currentIds.Contains(Entries[i].AvatarId))
            {
                Entries.RemoveAt(i);
            }
        }
    }
}

public sealed class AvatarLibraryEntry : ObservableObject
{
    private string avatarId = string.Empty;
    private string customIconPath = string.Empty;
    private string groupId = string.Empty;
    private List<string> tagIds = [];

    public string AvatarId
    {
        get => avatarId;
        set => SetProperty(ref avatarId, value);
    }

    public string CustomIconPath
    {
        get => customIconPath;
        set => SetProperty(ref customIconPath, value);
    }

    public string GroupId
    {
        get => groupId;
        set => SetProperty(ref groupId, value);
    }

    public List<string> TagIds
    {
        get => tagIds;
        set => SetProperty(ref tagIds, value ?? []);
    }
}

public sealed class AvatarGroup : ObservableObject
{
    private string id = Guid.NewGuid().ToString();
    private string name = string.Empty;
    private int sortOrder;

    public string Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public int SortOrder
    {
        get => sortOrder;
        set => SetProperty(ref sortOrder, value);
    }
}

public sealed class AvatarTag : ObservableObject
{
    private string id = Guid.NewGuid().ToString();
    private string name = string.Empty;
    private string colorHex = "#A855F7";

    public string Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string ColorHex
    {
        get => colorHex;
        set => SetProperty(ref colorHex, value);
    }
}