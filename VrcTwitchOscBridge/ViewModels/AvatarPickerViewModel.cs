using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public enum BrowseSection
{
    AllAvatars,
    Recent,
    Favorites,
    FavoritesGroup1,
    FavoritesGroup2,
    FavoritesGroup3,
    FavoritesGroup4,
    Uploaded,
    Purchased,
    LocalOsc,
    UserGroup,
    Ungrouped
}

public sealed record SidebarItem(
    string Label,
    BrowseSection Section,
    string Icon,
    int Count,
    bool IsExpandable = false,
    bool IsExpanded = false,
    IReadOnlyList<SidebarItem>? Children = null,
    string? ColorHex = null
);

public sealed class AvatarPickerViewModel : ObservableObject
{
    private readonly AvatarImageService imageService;
    private readonly AvatarLibrary? avatarLibrary;
    private readonly IReadOnlyList<VrChatAvatarSummary> avatarSummaries;
    private readonly IReadOnlyList<VrChatFavoriteGroup>? favoriteGroups;
    private readonly IReadOnlyDictionary<string, string>? avatarFavoriteGroups;
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
        IReadOnlyList<string>? multiSelectCurrentIds = null,
        IReadOnlyList<VrChatFavoriteGroup>? favoriteGroups = null,
        IReadOnlyDictionary<string, string>? avatarFavoriteGroups = null)
    {
        this.imageService = imageService;
        this.avatarLibrary = avatarLibrary;
        this.avatarSummaries = avatars;
        this.favoriteGroups = favoriteGroups;
        this.avatarFavoriteGroups = avatarFavoriteGroups;

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
                var selected = current with { IsSelected = true };
                var index = AllAvatars.IndexOf(current);
                if (index >= 0) AllAvatars[index] = selected;
            }
        }

        viewMode = avatarLibrary?.LastViewMode ?? AvatarPickerViewMode.Grid;

        CollectFilterTags();
        BuildSidebarItems();
        selectedSidebarItem = SidebarItems.FirstOrDefault(s => s.Section == BrowseSection.AllAvatars);
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
                                var updated = av with { Image = img };
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

    public void CancelImageLoading()
    {
        imageLoadCancellation?.Cancel();
        imageLoadCancellation?.Dispose();
        imageLoadCancellation = null;
    }

    public async Task RefreshAllImagesAsync()
    {
        CancelImageLoading();
        imageService.ClearDiskCache();
        imageService.ClearCache();

        var placeholder = imageService.GetPlaceholderImage();
        for (var i = 0; i < AllAvatars.Count; i++)
        {
            var avatar = AllAvatars[i];
            var reset = avatar with { Image = placeholder };
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
                var previous = FilteredAvatars.FirstOrDefault(a => string.Equals(a.Id, selectedAvatarId, StringComparison.Ordinal));
                if (previous is not null)
                {
                    var cleared = previous with { IsSelected = false };
                    var prevIndex = AllAvatars.IndexOf(previous);
                    if (prevIndex >= 0) AllAvatars[prevIndex] = cleared;
                    var prevFilteredIndex = FilteredAvatars.IndexOf(previous);
                    if (prevFilteredIndex >= 0) FilteredAvatars[prevFilteredIndex] = cleared;
                }

                selectedAvatarId = value.Id;
                selectedAvatarName = value.Name;

                var selected = value with { IsSelected = true };
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

    public ObservableCollection<SidebarItem> SidebarItems { get; private set; } = [];

    private SidebarItem? selectedSidebarItem;
    public SidebarItem? SelectedSidebarItem
    {
        get => selectedSidebarItem;
        set
        {
            if (SetProperty(ref selectedSidebarItem, value))
            {
                ApplyFilter();
                RaisePropertyChanged(nameof(SectionTitle));
                RaisePropertyChanged(nameof(SectionDescription));
            }
        }
    }

    public ObservableCollection<string> AllStyleTags { get; } = [];
    public ObservableCollection<string> AllContentTags { get; } = [];
    public ObservableCollection<string> SelectedStyleTags { get; } = [];
    public ObservableCollection<string> SelectedContentTags { get; } = [];

    private string? selectedPlatform;
    public string? SelectedPlatform
    {
        get => selectedPlatform;
        set
        {
            if (SetProperty(ref selectedPlatform, value))
                ApplyFilter();
        }
    }

    private bool filtersExpanded;
    public bool FiltersExpanded
    {
        get => filtersExpanded;
        set => SetProperty(ref filtersExpanded, value);
    }

    public string SectionTitle => SelectedSidebarItem?.Label ?? "All Avatars";
    public string SectionDescription => GetSectionDescription();

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
        var section = selectedSidebarItem?.Section ?? BrowseSection.AllAvatars;
        var recentIds = avatarLibrary?.RecentAvatarIds ?? [];

        foreach (var avatar in AllAvatars)
        {
            if (!MatchesSection(avatar, section, recentIds)) continue;

            if (!string.IsNullOrWhiteSpace(search))
            {
                var matchesSearch = avatar.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase);
                if (!matchesSearch) continue;
            }

            if (SelectedStyleTags.Count > 0 && !SelectedStyleTags.Any(t => avatar.StyleTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                continue;

            if (SelectedContentTags.Count > 0 && !SelectedContentTags.Any(t => avatar.ContentTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                continue;

            if (!string.IsNullOrWhiteSpace(selectedPlatform) && !string.Equals(avatar.Platform, selectedPlatform, StringComparison.OrdinalIgnoreCase))
                continue;

            FilteredAvatars.Add(avatar);
        }

        RaisePropertyChanged(nameof(FilteredCountText));
    }

    private bool MatchesSection(AvatarPickerItem avatar, BrowseSection section, IReadOnlyList<string> recentIds)
    {
        return section switch
        {
            BrowseSection.AllAvatars => true,
            BrowseSection.Recent => recentIds.Contains(avatar.Id),
            BrowseSection.Favorites => avatar.IsFavorited,
            BrowseSection.FavoritesGroup1 or BrowseSection.FavoritesGroup2
                or BrowseSection.FavoritesGroup3 or BrowseSection.FavoritesGroup4
                => avatar.IsFavorited && avatar.FavoriteGroupName == GetFavoriteGroupNameForSection(section),
            BrowseSection.Uploaded => avatar.IsUploaded,
            BrowseSection.Purchased => avatar.IsLicensed,
            BrowseSection.LocalOsc => !avatar.IsUploaded && !avatar.IsFavorited && !avatar.IsLicensed,
            BrowseSection.UserGroup => MatchesUserGroup(avatar),
            BrowseSection.Ungrouped => string.IsNullOrWhiteSpace(avatarLibrary?.GetEntry(avatar.Id)?.GroupId),
            _ => true
        };
    }

    private string? GetFavoriteGroupNameForSection(BrowseSection section)
    {
        var index = section - BrowseSection.FavoritesGroup1;
        if (index >= 0 && index < (favoriteGroups?.Count ?? 0))
            return favoriteGroups![index].DisplayName;
        return null;
    }

    private bool MatchesUserGroup(AvatarPickerItem avatar)
    {
        if (avatarLibrary is null || SelectedSidebarItem is null) return false;
        var groupId = avatarLibrary.Groups
            .FirstOrDefault(g => g.Name == SelectedSidebarItem.Label)?.Id;
        return groupId is not null && avatarLibrary.GetEntry(avatar.Id)?.GroupId == groupId;
    }

    private string GetSectionDescription()
    {
        var section = selectedSidebarItem?.Section ?? BrowseSection.AllAvatars;
        return section switch
        {
            BrowseSection.AllAvatars => LocalizationService.Translate("All avatars from your VRChat account"),
            BrowseSection.Recent => LocalizationService.Translate("Recently selected avatars"),
            BrowseSection.Favorites => LocalizationService.Translate("Your favorited avatars"),
            BrowseSection.FavoritesGroup1 or BrowseSection.FavoritesGroup2
                or BrowseSection.FavoritesGroup3 or BrowseSection.FavoritesGroup4
                => LocalizationService.Translate("Avatars in this favorites group"),
            BrowseSection.Uploaded => LocalizationService.Translate("Avatars you have uploaded"),
            BrowseSection.Purchased => LocalizationService.Translate("Avatars you have purchased or licensed"),
            BrowseSection.LocalOsc => LocalizationService.Translate("Avatars detected via local OSC cache"),
            BrowseSection.UserGroup => LocalizationService.Translate("Avatars in your custom user group"),
            BrowseSection.Ungrouped => LocalizationService.Translate("Avatars not assigned to any group"),
            _ => string.Empty
        };
    }

    private void CollectFilterTags()
    {
        var styleTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var summary in avatarSummaries)
        {
            foreach (var tag in summary.StyleTags)
                styleTags.Add(tag);
            foreach (var tag in summary.ContentTags)
                contentTags.Add(tag);
        }

        AllStyleTags.Clear();
        foreach (var tag in styleTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            AllStyleTags.Add(tag);

        AllContentTags.Clear();
        foreach (var tag in contentTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            AllContentTags.Add(tag);
    }

    private void BuildSidebarItems()
    {
        var items = new List<SidebarItem>();

        // ── BROWSE section ──────────────────────────────────────────
        items.Add(new SidebarItem("", BrowseSection.AllAvatars, "", 0));
        items.Add(new SidebarItem(
            LocalizationService.Translate("All Avatars"),
            BrowseSection.AllAvatars,
            "\uE80F",
            AllAvatars.Count));

        var recentCount = avatarLibrary?.RecentAvatarIds.Count ?? 0;
        items.Add(new SidebarItem(
            LocalizationService.Translate("Recent"),
            BrowseSection.Recent,
            "\uE81C",
            recentCount));

        // ── SOURCES section ─────────────────────────────────────────
        items.Add(new SidebarItem("", BrowseSection.AllAvatars, "", 0));

        var favoritesCount = AllAvatars.Count(a => a.IsFavorited);
        var favChildren = new List<SidebarItem>();
        if (favoriteGroups is not null)
        {
            var favSections = new[]
            {
                BrowseSection.FavoritesGroup1,
                BrowseSection.FavoritesGroup2,
                BrowseSection.FavoritesGroup3,
                BrowseSection.FavoritesGroup4
            };
            for (var i = 0; i < favoriteGroups.Count && i < favSections.Length; i++)
            {
                var group = favoriteGroups[i];
                var groupCount = AllAvatars.Count(a =>
                    a.IsFavorited && string.Equals(a.FavoriteGroupName, group.DisplayName, StringComparison.Ordinal));
                favChildren.Add(new SidebarItem(
                    group.DisplayName,
                    favSections[i],
                    "\uE734",
                    groupCount,
                    ColorHex: "#F472B6"));
            }
        }

        // Favorites parent
        items.Add(new SidebarItem(
            LocalizationService.Translate("Favorites"),
            BrowseSection.Favorites,
            "\uE734",
            favoritesCount,
            IsExpandable: favChildren.Count > 0));

        // Favorites sub-groups (indented, flattened into list)
        foreach (var child in favChildren)
        {
            items.Add(new SidebarItem(
                child.Label,
                child.Section,
                "\uE734",
                child.Count,
                ColorHex: child.ColorHex,
                IsExpanded: true));
        }

        var uploadedCount = AllAvatars.Count(a => a.IsUploaded);
        items.Add(new SidebarItem(
            LocalizationService.Translate("Uploaded"),
            BrowseSection.Uploaded,
            "\uE7B7",
            uploadedCount));

        var purchasedCount = AllAvatars.Count(a => a.IsLicensed);
        items.Add(new SidebarItem(
            LocalizationService.Translate("Purchased"),
            BrowseSection.Purchased,
            "\uE738",
            purchasedCount));

        var localOscCount = AllAvatars.Count(a => !a.IsUploaded && !a.IsFavorited && !a.IsLicensed);
        items.Add(new SidebarItem(
            LocalizationService.Translate("Local OSC"),
            BrowseSection.LocalOsc,
            "\U0001F4BB",
            localOscCount));

        // ── MY GROUPS section ───────────────────────────────────────
        if (avatarLibrary?.Groups is { Count: > 0 })
        {
            items.Add(new SidebarItem("", BrowseSection.AllAvatars, "", 0));

            foreach (var group in avatarLibrary.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
            {
                var count = AllAvatars.Count(a => avatarLibrary.GetEntry(a.Id)?.GroupId == group.Id);
                items.Add(new SidebarItem(group.Name, BrowseSection.UserGroup,
                    "", count));
            }
        }

        var ungroupedCount = AllAvatars.Count(a => string.IsNullOrWhiteSpace(avatarLibrary?.GetEntry(a.Id)?.GroupId));
        items.Add(new SidebarItem(
            LocalizationService.Translate("Ungrouped"),
            BrowseSection.Ungrouped,
            "\U0001F4C2",
            ungroupedCount));

        SidebarItems = new ObservableCollection<SidebarItem>(items);
        RaisePropertyChanged(nameof(SidebarItems));
    }

    public void RebuildFilterOptions()
    {
        CollectFilterTags();
        BuildSidebarItems();
        if (selectedSidebarItem is null)
            selectedSidebarItem = SidebarItems.FirstOrDefault(s => s.Section == BrowseSection.AllAvatars);
        RaisePropertyChanged(nameof(SelectedSidebarItem));
    }

    private AvatarPickerItem CreatePickerItem(VrChatAvatarSummary summary)
    {
        var image = imageService.GetPlaceholderImage();
        var entry = avatarLibrary?.GetEntry(summary.Id);
        var tags = ResolveTags(entry);
        var favGroupName = avatarFavoriteGroups?.GetValueOrDefault(summary.Id);
        return new AvatarPickerItem(
            summary.Id,
            summary.Name,
            summary.AuthorName,
            image,
            summary.ThumbnailUrl,
            IsSelected: string.Equals(summary.Id, selectedAvatarId, StringComparison.Ordinal),
            summary.IsCurrentAvatar,
            IsUploaded: summary.IsUploaded,
            summary.IsFavorited,
            IsLicensed: summary.IsLicensed,
            favGroupName,
            summary.Platform,
            summary.StyleTags,
            summary.ContentTags,
            UserTags: tags);
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

    public void RebuildItem(AvatarPickerItem item)
    {
        var entry = avatarLibrary?.GetEntry(item.Id);
        var tags = ResolveTags(entry);
        var updated = item with { UserTags = tags };

        var allIndex = AllAvatars.IndexOf(item);
        if (allIndex >= 0) AllAvatars[allIndex] = updated;

        var filteredIndex = FilteredAvatars.IndexOf(item);
        if (filteredIndex >= 0) FilteredAvatars[filteredIndex] = updated;
    }
}

public sealed record AvatarPickerItem(
    string Id,
    string Name,
    string AuthorName,
    ImageSource? Image,
    string? ThumbnailUrl,
    bool IsSelected,
    bool IsCurrentAvatar,
    bool IsUploaded,
    bool IsFavorited,
    bool IsLicensed,
    string? FavoriteGroupName,
    string Platform,
    IReadOnlyList<string> StyleTags,
    IReadOnlyList<string> ContentTags,
    IReadOnlyList<AvatarTagDisplay>? UserTags = null)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, Id, StringComparison.Ordinal)
        ? Name
        : "Unknown Avatar";

    public string SearchText => $"{Id} {Name} {AuthorName} {FavoriteGroupName} {string.Join(" ", StyleTags)} {string.Join(" ", ContentTags)}";

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

    public IReadOnlyList<AvatarTagDisplay>? Tags => UserTags;
}
