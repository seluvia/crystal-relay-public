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
    private AvatarLibraryEntry? selectedEntry;
    private AvatarGroup? selectedGroup;
    private AvatarTag? selectedTag;

    public AvatarLibraryManagerViewModel(AvatarLibrary library, AvatarImageService imageService)
    {
        this.library = library;
        this.imageService = imageService;
        AddGroupCommand = new RelayCommand(AddGroup);
        DeleteGroupCommand = new RelayCommand(DeleteGroup, () => SelectedGroup is not null);
        AddTagCommand = new RelayCommand(AddTag);
        DeleteTagCommand = new RelayCommand(DeleteTag, () => SelectedTag is not null);
    }

    public ObservableCollection<Models.AvatarLibraryEntry> Entries => library.Entries;
    public ObservableCollection<AvatarGroup> Groups => library.Groups;
    public ObservableCollection<AvatarTag> Tags => library.Tags;

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

    public RelayCommand AddGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand AddTagCommand { get; }
    public RelayCommand DeleteTagCommand { get; }

    public void AddGroup()
    {
        var group = new AvatarGroup
        {
            Name = $"Group {Groups.Count + 1}",
            SortOrder = Groups.Count
        };
        Groups.Add(group);
        SelectedGroup = group;
    }

    public void DeleteGroup()
    {
        if (SelectedGroup is null) return;
        var id = SelectedGroup.Id;
        Groups.Remove(SelectedGroup);
        foreach (var entry in Entries)
        {
            entry.GroupIds.Remove(id);
        }
        SelectedGroup = Groups.FirstOrDefault();
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
}