using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarLibraryManagerViewModel : ObservableObject
{
    private readonly AvatarLibrary library;
    private readonly AvatarImageService imageService;
    private readonly IReadOnlyList<VrChatAvatarSummary> avatars;
    private AvatarLibraryEntry? selectedEntry;
    private AvatarGroup? selectedGroup;
    private AvatarTag? selectedTag;
    private string avatarSearchText = string.Empty;
    private AvatarAssignmentRow? selectedAssignmentRow;
    private FilterOption? selectedGroupAssignment;
    private string selectionCountText = string.Empty;

    public AvatarLibraryManagerViewModel(
        AvatarLibrary library,
        AvatarImageService imageService,
        IReadOnlyList<VrChatAvatarSummary> avatars)
    {
        this.library = library;
        this.imageService = imageService;
        this.avatars = avatars;

        AddGroupCommand = new RelayCommand(AddGroup);
        DeleteGroupCommand = new RelayCommand(DeleteGroup, () => SelectedGroup is not null);
        AddTagCommand = new RelayCommand(AddTag);
        DeleteTagCommand = new RelayCommand(DeleteTag, () => SelectedTag is not null);
        ApplyAssignmentCommand = new RelayCommand(ApplyAssignment, () => SelectedAssignmentRow is not null);
        ClearGroupCommand = new RelayCommand(ClearGroupForSelection, () => SelectedAssignmentRow is not null);
        ClearTagsCommand = new RelayCommand(ClearTagsForSelection, () => SelectedAssignmentRow is not null);
        SelectAllCommand = new RelayCommand(SelectAllRows);
        SelectNoneCommand = new RelayCommand(SelectNoneRows);
        SetCustomIconForSelectionCommand = new RelayCommand(SetCustomIconForSelection, () => SelectedAssignmentRow is not null);
        ClearCustomIconForSelectionCommand = new RelayCommand(ClearCustomIconForSelection, () => SelectedAssignmentRow is not null);

        RebuildAssignmentRows();
        RebuildGroupAssignmentOptions();
        RebuildTagAssignmentOptions();
    }

    public ObservableCollection<Models.AvatarLibraryEntry> Entries => library.Entries;
    public ObservableCollection<AvatarGroup> Groups => library.Groups;
    public ObservableCollection<AvatarTag> Tags => library.Tags;
    public ObservableCollection<AvatarAssignmentRow> AssignmentRows { get; } = [];
    public ObservableCollection<FilterOption> GroupAssignmentOptions { get; } = [];
    public ObservableCollection<TagAssignmentOption> TagAssignmentOptions { get; } = [];

    public AvatarLibraryEntry? SelectedEntry
    {
        get => selectedEntry;
        set => SetProperty(ref selectedEntry, value);
    }

    public AvatarGroup? SelectedGroup
    {
        get => selectedGroup;
        set
        {
            if (SetProperty(ref selectedGroup, value))
            {
                DeleteGroupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AvatarTag? SelectedTag
    {
        get => selectedTag;
        set
        {
            if (SetProperty(ref selectedTag, value))
            {
                DeleteTagCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AvatarAssignmentRow? SelectedAssignmentRow
    {
        get => selectedAssignmentRow;
        set
        {
            if (SetProperty(ref selectedAssignmentRow, value))
            {
                UpdateAssignmentPaneFromSelection();
                ApplyAssignmentCommand.NotifyCanExecuteChanged();
                ClearGroupCommand.NotifyCanExecuteChanged();
                ClearTagsCommand.NotifyCanExecuteChanged();
                SetCustomIconForSelectionCommand.NotifyCanExecuteChanged();
                ClearCustomIconForSelectionCommand.NotifyCanExecuteChanged();
                UpdateSelectionCount();
            }
        }
    }

    public FilterOption? SelectedGroupAssignment
    {
        get => selectedGroupAssignment;
        set => SetProperty(ref selectedGroupAssignment, value);
    }

    public string AvatarSearchText
    {
        get => avatarSearchText;
        set
        {
            if (SetProperty(ref avatarSearchText, value))
            {
                RebuildAssignmentRows();
            }
        }
    }

    public string SelectionCountText
    {
        get => selectionCountText;
        private set => SetProperty(ref selectionCountText, value);
    }

    public RelayCommand AddGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand AddTagCommand { get; }
    public RelayCommand DeleteTagCommand { get; }
    public RelayCommand ApplyAssignmentCommand { get; }
    public RelayCommand ClearGroupCommand { get; }
    public RelayCommand ClearTagsCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand SetCustomIconForSelectionCommand { get; }
    public RelayCommand ClearCustomIconForSelectionCommand { get; }

    public void AddGroup()
    {
        var group = new AvatarGroup
        {
            Name = $"Group {Groups.Count + 1}",
            SortOrder = Groups.Count
        };
        Groups.Add(group);
        SelectedGroup = group;
        RebuildGroupAssignmentOptions();
    }

    public void DeleteGroup()
    {
        if (SelectedGroup is null) return;
        var id = SelectedGroup.Id;
        Groups.Remove(SelectedGroup);
        foreach (var entry in Entries)
        {
            if (entry.GroupId == id)
            {
                entry.GroupId = string.Empty;
            }
        }
        SelectedGroup = Groups.FirstOrDefault();
        RebuildGroupAssignmentOptions();
        RebuildAssignmentRows();
    }

    public void AddTag()
    {
        var tag = new AvatarTag
        {
            Name = $"Tag {Tags.Count + 1}",
            ColorHex = "#A855F7"
        };
        Tags.Add(tag);
        SelectedTag = tag;
        RebuildTagAssignmentOptions();
    }

    public void DeleteTag()
    {
        if (SelectedTag is null) return;
        var id = SelectedTag.Id;
        Tags.Remove(SelectedTag);
        foreach (var entry in Entries)
        {
            entry.TagIds.Remove(id);
        }
        SelectedTag = Tags.FirstOrDefault();
        RebuildTagAssignmentOptions();
        RebuildAssignmentRows();
    }

    private static readonly Regex HexColorRegex = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public bool IsValidHexColor(string hex) => HexColorRegex.IsMatch(hex);

    public void SetCustomIconForEntry(AvatarLibraryEntry entry, Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
            Title = "Choose Avatar Icon"
        };

        if (dialog.ShowDialog(owner) != true) return;

        var relativePath = imageService.SaveCustomIcon(entry.AvatarId, dialog.FileName);
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            entry.CustomIconPath = relativePath;
        }
    }

    public void ClearCustomIconForEntry(AvatarLibraryEntry entry)
    {
        entry.CustomIconPath = string.Empty;
    }

    // --- Assignment row management ---

    private void RebuildAssignmentRows()
    {
        AssignmentRows.Clear();
        var search = avatarSearchText.Trim().ToLowerInvariant();

        // Ensure every avatar in the VRChat list has an entry (so it shows up).
        foreach (var avatar in avatars)
        {
            library.EnsureEntry(avatar.Id);
        }

        foreach (var avatar in avatars)
        {
            if (!string.IsNullOrWhiteSpace(search)
                && !avatar.Name.ToLowerInvariant().Contains(search)
                && !avatar.Id.ToLowerInvariant().Contains(search))
            {
                continue;
            }

            var entry = library.GetEntry(avatar.Id);
            if (entry is null) continue;

            var groupName = string.IsNullOrWhiteSpace(entry.GroupId)
                ? string.Empty
                : library.Groups.FirstOrDefault(g => g.Id == entry.GroupId)?.Name ?? string.Empty;

            var tags = entry.TagIds
                .Select(id => library.Tags.FirstOrDefault(t => t.Id == id))
                .Where(t => t is not null)
                .Select(t => new AvatarTagDisplay(t!.Id, t!.Name, t!.ColorHex))
                .ToList();

            var image = imageService.GetAvatarImage(avatar.Id, entry.CustomIconPath, avatar.ThumbnailUrl);

            AssignmentRows.Add(new AvatarAssignmentRow
            {
                Entry = entry,
                DisplayName = avatar.Name,
                Image = image,
                AvatarId = avatar.Id,
                GroupName = groupName,
                Tags = tags
            });
        }

        UpdateSelectionCount();
    }

    private void UpdateAssignmentPaneFromSelection()
    {
        var row = SelectedAssignmentRow;
        if (row is null) return;

        var entry = row.Entry;
        SelectedGroupAssignment = GroupAssignmentOptions.FirstOrDefault(o => o.Id == entry.GroupId)
            ?? GroupAssignmentOptions.FirstOrDefault();

        foreach (var tagOpt in TagAssignmentOptions)
        {
            tagOpt.IsChecked = entry.TagIds.Contains(tagOpt.TagId);
        }
    }

    private void UpdateSelectionCount()
    {
        SelectionCountText = SelectedAssignmentRow is not null
            ? $"1 {LocalizationService.Translate("selected")}"
            : LocalizationService.Translate("Select avatars to assign");
    }

    private void RebuildGroupAssignmentOptions()
    {
        GroupAssignmentOptions.Clear();
        GroupAssignmentOptions.Add(new FilterOption(string.Empty, LocalizationService.Translate("— No group —")));
        foreach (var group in library.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
        {
            GroupAssignmentOptions.Add(new FilterOption(group.Id, group.Name));
        }
    }

    private void RebuildTagAssignmentOptions()
    {
        TagAssignmentOptions.Clear();
        foreach (var tag in library.Tags.OrderBy(t => t.Name))
        {
            TagAssignmentOptions.Add(new TagAssignmentOption(tag.Id, tag.Name, tag.ColorHex));
        }
    }

    public void ApplyAssignment()
    {
        var row = SelectedAssignmentRow;
        if (row is null) return;

        var groupId = SelectedGroupAssignment?.Id ?? string.Empty;
        row.Entry.GroupId = groupId;
        row.GroupName = string.IsNullOrWhiteSpace(groupId)
            ? string.Empty
            : library.Groups.FirstOrDefault(g => g.Id == groupId)?.Name ?? string.Empty;

        // Apply tags (toggle-based: whatever is checked becomes the new set).
        row.Entry.TagIds.Clear();
        foreach (var tagOpt in TagAssignmentOptions.Where(t => t.IsChecked))
        {
            row.Entry.TagIds.Add(tagOpt.TagId);
        }
        var tags = row.Entry.TagIds
            .Select(id => library.Tags.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Select(t => new AvatarTagDisplay(t!.Id, t!.Name, t!.ColorHex))
            .ToList();
        row.Tags = tags;
    }

    public void ClearGroupForSelection()
    {
        var row = SelectedAssignmentRow;
        if (row is null) return;
        row.Entry.GroupId = string.Empty;
        row.GroupName = string.Empty;
    }

    public void ClearTagsForSelection()
    {
        var row = SelectedAssignmentRow;
        if (row is null) return;
        row.Entry.TagIds.Clear();
        row.Tags = [];
    }

    public void SelectAllRows()
    {
        // Multi-select via SelectAll is handled by the view's ListBox.
        // The VM raises property changes for the count display.
        UpdateSelectionCount();
    }

    public void SelectNoneRows()
    {
        SelectedAssignmentRow = null;
        UpdateSelectionCount();
    }

    public void SetCustomIconForSelection()
    {
        var row = SelectedAssignmentRow;
        if (row is null) return;
        SetCustomIconForEntry(row.Entry, Application.Current.MainWindow);
        RebuildAssignmentRows();
    }

    public void ClearCustomIconForSelection()
    {
        var row = SelectedAssignmentRow;
        if (row is null) return;
        ClearCustomIconForEntry(row.Entry);
        RebuildAssignmentRows();
    }
}

public sealed class TagAssignmentOption : ObservableObject
{
    private bool isChecked;

    public string TagId { get; }
    public string Display { get; }
    public string ColorHex { get; }

    public TagAssignmentOption(string tagId, string display, string colorHex)
    {
        TagId = tagId;
        Display = display;
        ColorHex = colorHex;
    }

    public bool IsChecked
    {
        get => isChecked;
        set => SetProperty(ref isChecked, value);
    }
}
