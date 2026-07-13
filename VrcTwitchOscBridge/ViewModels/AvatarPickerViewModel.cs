using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarPickerViewModel : ObservableObject
{
    private readonly AvatarImageService imageService;
    private readonly AvatarLibrary? avatarLibrary;
    private readonly IReadOnlyList<VrChatAvatarSummary> avatarSummaries;
    private string searchText = string.Empty;
    private AvatarPickerViewMode viewMode = AvatarPickerViewMode.Grid;
    private string? selectedFilterGroupId;
    private string? selectedFilterTagId;
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
        this.avatarSummaries = avatars;

        // Prune library entries whose avatar is no longer in the VRChat list.
        avatarLibrary?.PruneMissingEntries(avatars);

        if (multiSelectCurrentIds is { Count: > 0 })
        {
            isMultiSelectMode = true;
            SelectedMultiAvatarIds = new List<string>(multiSelectCurrentIds);
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
                var selected = new AvatarPickerItem(current.Id, current.Name, current.SourceLabel, current.Image, current.ThumbnailUrl, true, current.Tags);
                var index = AllAvatars.IndexOf(current);
                if (index >= 0) AllAvatars[index] = selected;
            }
        }

        viewMode = avatarLibrary?.LastViewMode ?? AvatarPickerViewMode.Grid;

        RebuildFilterOptions();
        ApplyFilter();
    }

    private CancellationTokenSource? imageLoadCancellation;
    private bool isImageLoading;

    public bool IsImageLoading
    {
        get => isImageLoading;
        private set
        {
            if (isImageLoading != value)
            {
                isImageLoading = value;
                RaisePropertyChanged(nameof(IsImageLoading));
            }
        }
    }

    /// <summary>
    /// Loads VRChat thumbnail images in parallel (up to 3 concurrent downloads).
    /// Call after the window is shown.
    /// </summary>
    public async Task LoadImagesAsync()
    {
        imageLoadCancellation?.Cancel();
        imageLoadCancellation?.Dispose();
        var cts = new CancellationTokenSource();
        imageLoadCancellation = cts;
        IsImageLoading = true;
        var cancellationToken = cts.Token;

        var dispatcher = Application.Current.Dispatcher;
        int loaded = 0, noUrl = 0, failed = 0;
        var avatarSnapshot = AllAvatars.ToList();
        using var semaphore = new SemaphoreSlim(3);

        var tasks = avatarSnapshot.Select(async avatar =>
        {
            if (cancellationToken.IsCancellationRequested) return;

            var thumbnailUrl = avatar.ThumbnailUrl;
            if (string.IsNullOrWhiteSpace(thumbnailUrl))
            {
                Interlocked.Increment(ref noUrl);
                return;
            }

            try
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var newImage = await imageService
                        .GetAvatarImageAsync(avatar.Id, customIconPath: null, thumbnailUrl, cancellationToken)
                        .ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested) return;

                    if (newImage is not null && !IsPlaceholderImage(newImage))
                    {
                        var img = newImage;
                        var av = avatar;
                        await dispatcher.InvokeAsync(() =>
                        {
                            if (cancellationToken.IsCancellationRequested) return;
                            var index = AllAvatars.IndexOf(av);
                            if (index >= 0)
                            {
                                var updated = new AvatarPickerItem(av.Id, av.Name, av.SourceLabel, img, av.ThumbnailUrl, av.IsSelected, av.Tags);
                                AllAvatars[index] = updated;
                                var filteredIndex = FilteredAvatars.IndexOf(av);
                                if (filteredIndex >= 0)
                                    FilteredAvatars[filteredIndex] = updated;
                            }
                        });
                        Interlocked.Increment(ref loaded);
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        Debug.WriteLine($"[AvatarPicker] Image loading complete: {loaded} loaded, {noUrl} no URL, {failed} failed");

        await dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(imageLoadCancellation, cts))
                IsImageLoading = false;
        });
    }

    private static bool IsPlaceholderImage(ImageSource? image)
    {
        if (image is DrawingImage drawingImage)
        {
            return drawingImage.Drawing is DrawingGroup;
        }
        return false;
    }

    /// <summary>
    /// Cancels any pending image loading.
    /// </summary>
    public void CancelImageLoading()
    {
        imageLoadCancellation?.Cancel();
        imageLoadCancellation?.Dispose();
        imageLoadCancellation = null;
    }

    /// <summary>
    /// Clears all cached thumbnails and re-downloads them from the VRChat API.
    /// </summary>
    public async Task RefreshAllImagesAsync()
    {
        CancelImageLoading();
        imageService.ClearDiskCache();
        imageService.ClearCache();

        var placeholder = imageService.GetPlaceholderImage();
        for (var i = 0; i < AllAvatars.Count; i++)
        {
            var avatar = AllAvatars[i];
            var reset = new AvatarPickerItem(avatar.Id, avatar.Name, avatar.SourceLabel, placeholder, avatar.ThumbnailUrl, avatar.IsSelected, avatar.Tags);
            AllAvatars[i] = reset;
        }

        ApplyFilter();

        await LoadImagesAsync();
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
                // Clear previous selection's IsSelected
                var previous = FilteredAvatars.FirstOrDefault(a => string.Equals(a.Id, selectedAvatarId, StringComparison.Ordinal));
                if (previous is not null)
                {
                    var cleared = new AvatarPickerItem(previous.Id, previous.Name, previous.SourceLabel, previous.Image, previous.ThumbnailUrl, false, previous.Tags);
                    var prevIndex = AllAvatars.IndexOf(previous);
                    if (prevIndex >= 0) AllAvatars[prevIndex] = cleared;
                    var prevFilteredIndex = FilteredAvatars.IndexOf(previous);
                    if (prevFilteredIndex >= 0) FilteredAvatars[prevFilteredIndex] = cleared;
                }

                selectedAvatarId = value.Id;
                selectedAvatarName = value.Name;

                // Set new selection's IsSelected
                var selected = new AvatarPickerItem(value.Id, value.Name, value.SourceLabel, value.Image, value.ThumbnailUrl, true, value.Tags);
                var index = AllAvatars.IndexOf(value);
                if (index >= 0) AllAvatars[index] = selected;
                var filteredIndex = FilteredAvatars.IndexOf(value);
                if (filteredIndex >= 0) FilteredAvatars[filteredIndex] = selected;

                RaisePropertyChanged(nameof(SelectedAvatarDisplayName));
                RaisePropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public List<string> SelectedMultiAvatarIds { get; } = [];

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

    public ObservableCollection<FilterOption> GroupFilterOptions { get; } = [];
    public ObservableCollection<FilterOption> TagFilterOptions { get; } = [];

    private FilterOption? selectedGroupFilterOption;
    private FilterOption? selectedTagFilterOption;

    public FilterOption? SelectedGroupFilterOption
    {
        get => selectedGroupFilterOption;
        set
        {
            if (SetProperty(ref selectedGroupFilterOption, value))
            {
                selectedFilterGroupId = value?.Id;
                ApplyFilter();
            }
        }
    }

    public FilterOption? SelectedTagFilterOption
    {
        get => selectedTagFilterOption;
        set
        {
            if (SetProperty(ref selectedTagFilterOption, value))
            {
                selectedFilterTagId = value?.Id;
                ApplyFilter();
            }
        }
    }

    public AvatarLibrary? Library => avatarLibrary;

    public IReadOnlyList<VrChatAvatarSummary> AvatarSummaries => avatarSummaries;

    public void RefreshFilter() => ApplyFilter();

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

    public void SelectAll()
    {
        if (!isMultiSelectMode) return;
        SelectedMultiAvatarIds.Clear();
        foreach (var avatar in AllAvatars)
        {
            if (!SelectedMultiAvatarIds.Contains(avatar.Id))
            {
                SelectedMultiAvatarIds.Add(avatar.Id);
            }
        }
        RaisePropertyChanged(nameof(CanConfirm));
        RaisePropertyChanged(nameof(MultiSelectCountText));
    }

    public void DeselectAll()
    {
        if (!isMultiSelectMode) return;
        SelectedMultiAvatarIds.Clear();
        RaisePropertyChanged(nameof(CanConfirm));
        RaisePropertyChanged(nameof(MultiSelectCountText));
    }

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
            var entry = avatarLibrary?.GetEntry(avatar.Id);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var groupName = entry is not null && !string.IsNullOrWhiteSpace(entry.GroupId)
                    ? avatarLibrary?.Groups.FirstOrDefault(g => g.Id == entry.GroupId)?.Name?.ToLowerInvariant()
                    : null;
                var tagNames = entry?.TagIds
                    .Select(id => avatarLibrary?.Tags.FirstOrDefault(t => t.Id == id)?.Name)
                    .Where(n => n is not null)
                    .Select(n => n!.ToLowerInvariant())
                    .ToList() ?? [];

                var matchesSearch = avatar.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (groupName is not null && groupName.Contains(search, StringComparison.OrdinalIgnoreCase))
                    || tagNames.Any(n => n.Contains(search, StringComparison.OrdinalIgnoreCase));

                if (!matchesSearch) continue;
            }

            // Group filter: null = all, "ungrouped" = empty GroupId, real id = exact match.
            if (!string.IsNullOrWhiteSpace(selectedFilterGroupId))
            {
                if (selectedFilterGroupId == "ungrouped")
                {
                    if (!string.IsNullOrWhiteSpace(entry?.GroupId)) continue;
                }
                else
                {
                    if (entry?.GroupId != selectedFilterGroupId) continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedFilterTagId))
            {
                if (entry?.TagIds.Contains(selectedFilterTagId) != true) continue;
            }

            // Ensure tags are fresh on the filtered item.
            var tags = ResolveTags(entry);
            var withTags = avatar.Tags is null || avatar.Tags.Count != tags.Count
                ? new AvatarPickerItem(avatar.Id, avatar.Name, avatar.SourceLabel, avatar.Image, avatar.ThumbnailUrl, avatar.IsSelected, tags)
                : avatar;

            FilteredAvatars.Add(withTags);
        }

        RaisePropertyChanged(nameof(FilteredCountText));
    }

    private AvatarPickerItem CreatePickerItem(VrChatAvatarSummary summary)
    {
        var image = imageService.GetPlaceholderImage();
        var entry = avatarLibrary?.GetEntry(summary.Id);
        var tags = ResolveTags(entry);
        return new AvatarPickerItem(
            summary.Id,
            summary.Name,
            summary.SourceLabel,
            image,
            summary.ThumbnailUrl,
            Tags: tags);
    }

    private IReadOnlyList<AvatarTagDisplay> ResolveTags(AvatarLibraryEntry? entry)
    {
        if (avatarLibrary is null || entry is null || entry.TagIds.Count == 0)
        {
            return [];
        }

        var tags = new List<AvatarTagDisplay>(entry.TagIds.Count);
        foreach (var tagId in entry.TagIds)
        {
            var tag = avatarLibrary.Tags.FirstOrDefault(t => t.Id == tagId);
            if (tag is not null)
            {
                tags.Add(new AvatarTagDisplay(tag.Id, tag.Name, tag.ColorHex));
            }
        }
        return tags;
    }

    /// <summary>
    /// Rebuilds an item with fresh Tags/Image/IsSelected and replaces it in both
    /// AllAvatars and FilteredAvatars. Consolidates the scattered replace logic.
    /// </summary>
    public void RebuildItem(AvatarPickerItem item)
    {
        var entry = avatarLibrary?.GetEntry(item.Id);
        var tags = ResolveTags(entry);
        var updated = new AvatarPickerItem(item.Id, item.Name, item.SourceLabel, item.Image, item.ThumbnailUrl, item.IsSelected, tags);

        var allIndex = AllAvatars.IndexOf(item);
        if (allIndex >= 0) AllAvatars[allIndex] = updated;

        var filteredIndex = FilteredAvatars.IndexOf(item);
        if (filteredIndex >= 0) FilteredAvatars[filteredIndex] = updated;
    }

    public void RebuildFilterOptions()
    {
        GroupFilterOptions.Clear();
        TagFilterOptions.Clear();

        GroupFilterOptions.Add(new FilterOption(null, LocalizationService.Translate("All")));
        GroupFilterOptions.Add(new FilterOption("ungrouped", LocalizationService.Translate("Ungrouped")));
        TagFilterOptions.Add(new FilterOption(null, LocalizationService.Translate("All")));
        if (avatarLibrary is not null)
        {
            foreach (var group in avatarLibrary.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
            {
                GroupFilterOptions.Add(new FilterOption(group.Id, group.Name));
            }

            foreach (var tag in avatarLibrary.Tags.OrderBy(t => t.Name))
            {
                TagFilterOptions.Add(new FilterOption(tag.Id, tag.Name));
            }
        }

        // Preserve current selection if still present, else reset to "All".
        selectedGroupFilterOption = GroupFilterOptions.FirstOrDefault(o => o.Id == selectedFilterGroupId)
            ?? GroupFilterOptions[0];
        selectedFilterGroupId = selectedGroupFilterOption.Id;
        selectedTagFilterOption = TagFilterOptions.FirstOrDefault(o => o.Id == selectedFilterTagId)
            ?? TagFilterOptions.FirstOrDefault() ?? new FilterOption(null, LocalizationService.Translate("All"));
        selectedFilterTagId = selectedTagFilterOption.Id;
        RaisePropertyChanged(nameof(SelectedGroupFilterOption));
        RaisePropertyChanged(nameof(SelectedTagFilterOption));
    }
}

public sealed record AvatarPickerItem(
    string Id,
    string Name,
    string SourceLabel,
    ImageSource? Image,
    string? ThumbnailUrl = null,
    bool IsSelected = false,
    IReadOnlyList<AvatarTagDisplay>? Tags = null)
{
    public string SearchText => $"{Id} {Name} {SourceLabel}";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, Id, StringComparison.Ordinal)
        ? Name
        : "Unknown Avatar";
}

public static class AvatarLibraryFilterOptionsBuilder
{
    public static IReadOnlyList<FilterOption> BuildGroupOptions(AvatarLibrary library)
    {
        var options = new List<FilterOption>
        {
            new(null, "All"),
            new("ungrouped", "Ungrouped")
        };
        foreach (var group in library.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
        {
            options.Add(new FilterOption(group.Id, group.Name));
        }
        return options;
    }

    public static IReadOnlyList<FilterOption> BuildTagOptions(AvatarLibrary library)
    {
        var options = new List<FilterOption> { new(null, "All") };
        foreach (var tag in library.Tags.OrderBy(t => t.Name))
        {
            options.Add(new FilterOption(tag.Id, tag.Name));
        }
        return options;
    }
}