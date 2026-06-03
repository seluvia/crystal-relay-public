using System.Collections.ObjectModel;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarPickerViewModel : ObservableObject
{
    private readonly AvatarImageService imageService;
    private readonly AvatarLibrary? avatarLibrary;
    private string searchText = string.Empty;
    private AvatarPickerViewMode viewMode = AvatarPickerViewMode.Grid;
    private string? selectedAvatarId;
    private string? selectedAvatarName;
    private bool isMultiSelectMode;

    public AvatarPickerViewModel(
        IReadOnlyList<VrChatAvatarSummary> avatars,
        AvatarImageService imageService,
        AvatarLibrary? avatarLibrary = null,
        string? currentAvatarId = null,
        IReadOnlyList<string>? multiSelectCurrentIds = null)
    {
        this.imageService = imageService;
        this.avatarLibrary = avatarLibrary;

        if (multiSelectCurrentIds is { Count: > 0 })
        {
            isMultiSelectMode = true;
            SelectedMultiAvatarIds = new HashSet<string>(multiSelectCurrentIds, StringComparer.Ordinal);
        }

        AllAvatars = new ObservableCollection<AvatarPickerItem>(
            avatars.Select(a => CreatePickerItem(a)));

        if (!string.IsNullOrWhiteSpace(currentAvatarId))
        {
            selectedAvatarId = currentAvatarId;
            var current = AllAvatars.FirstOrDefault(a => string.Equals(a.Id, currentAvatarId, StringComparison.Ordinal));
            if (current is not null)
            {
                selectedAvatarName = current.Name;
            }
        }

        viewMode = avatarLibrary?.LastViewMode ?? AvatarPickerViewMode.Grid;

        ApplyFilter();
    }

    public ObservableCollection<AvatarPickerItem> AllAvatars { get; }
    public ObservableCollection<AvatarPickerItem> FilteredAvatars { get; } = [];

    public AvatarPickerItem? SelectedItem
    {
        get => FilteredAvatars.FirstOrDefault(a => string.Equals(a.Id, selectedAvatarId, StringComparison.Ordinal));
        set
        {
            if (value is not null)
            {
                selectedAvatarId = value.Id;
                selectedAvatarName = value.Name;
                RaisePropertyChanged(nameof(SelectedAvatarDisplayName));
                RaisePropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public HashSet<string> SelectedMultiAvatarIds { get; } = [];

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public AvatarPickerViewMode ViewMode
    {
        get => viewMode;
        set
        {
            if (SetProperty(ref viewMode, value))
            {
                avatarLibrary?.LastViewMode = value;
            }
        }
    }

    public bool IsMultiSelectMode => isMultiSelectMode;
    public bool CanConfirm => isMultiSelectMode ? SelectedMultiAvatarIds.Count > 0 : !string.IsNullOrWhiteSpace(selectedAvatarId);
    public string SelectedAvatarDisplayName => selectedAvatarName ?? "No avatar selected";
    public string FilteredCountText => $"Showing {FilteredAvatars.Count} of {AllAvatars.Count} avatars";

    public void ToggleMultiSelect(AvatarPickerItem item)
    {
        if (SelectedMultiAvatarIds.Contains(item.Id))
        {
            SelectedMultiAvatarIds.Remove(item.Id);
        }
        else
        {
            SelectedMultiAvatarIds.Add(item.Id);
        }
        RaisePropertyChanged(nameof(CanConfirm));
        RaisePropertyChanged(nameof(MultiSelectCountText));
    }

    public string MultiSelectCountText => $"{SelectedMultiAvatarIds.Count} avatar{(SelectedMultiAvatarIds.Count == 1 ? string.Empty : "s")} in pool";

    public IReadOnlyList<string> GetSelectedAvatarIds() =>
        isMultiSelectMode
            ? SelectedMultiAvatarIds.ToList()
            : (string.IsNullOrWhiteSpace(selectedAvatarId) ? [] : [selectedAvatarId]);

    private void ApplyFilter()
    {
        FilteredAvatars.Clear();
        var search = searchText.Trim().ToLowerInvariant();

        foreach (var avatar in AllAvatars)
        {
            if (string.IsNullOrWhiteSpace(search) || avatar.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                FilteredAvatars.Add(avatar);
            }
        }

        RaisePropertyChanged(nameof(FilteredCountText));
    }

    private AvatarPickerItem CreatePickerItem(VrChatAvatarSummary summary)
    {
        var entry = avatarLibrary?.GetEntry(summary.Id);
        var customIconPath = entry?.CustomIconPath;

        var image = imageService.GetAvatarImage(summary.Id, customIconPath, vrchatThumbnailUrl: null);

        return new AvatarPickerItem(
            summary.Id,
            summary.Name,
            summary.SourceLabel,
            image);
    }
}

public sealed record AvatarPickerItem(
    string Id,
    string Name,
    string SourceLabel,
    ImageSource? Image)
{
    public string SearchText => $"{Id} {Name} {SourceLabel}";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, Id, StringComparison.Ordinal)
        ? Name
        : "Unknown Avatar";
}