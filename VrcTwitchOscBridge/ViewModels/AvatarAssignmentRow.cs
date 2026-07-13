using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

/// <summary>
/// One row in the manager's Avatars tab. Wraps a library entry with
/// display fields (avatar name, image, group name, tag chips).
/// </summary>
public sealed class AvatarAssignmentRow : ObservableObject
{
    private string groupName = string.Empty;
    private IReadOnlyList<AvatarTagDisplay> tags = [];

    public required AvatarLibraryEntry Entry { get; init; }
    public required string DisplayName { get; init; }
    public required ImageSource? Image { get; init; }
    public required string AvatarId { get; init; }

    public string GroupName
    {
        get => groupName;
        set => SetProperty(ref groupName, value);
    }

    public IReadOnlyList<AvatarTagDisplay> Tags
    {
        get => tags;
        set => SetProperty(ref tags, value);
    }
}
