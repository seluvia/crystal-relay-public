using System.Collections.ObjectModel;
using System.Windows.Controls;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public partial class RoulettePoolEditorControl : UserControl
{
    public RoulettePoolEditorControl()
    {
        InitializeComponent();
        DataContext = new RoulettePoolEditorViewModel();
    }

    public RoulettePoolEditorControl(AvatarRouletteProfile profile) : this()
    {
        ((RoulettePoolEditorViewModel)DataContext).Profile = profile;
    }
}

public sealed class RoulettePoolEditorViewModel : ObservableObject
{
    private AvatarRouletteProfile? _profile;

    public ObservableCollection<RouletteAvatarEntry> Pool { get; } = new();

    public AvatarRouletteProfile? Profile
    {
        get => _profile;
        set
        {
            if (_profile != value)
            {
                _profile = value;
                if (_profile != null)
                {
                    // Clear existing pool and add items from profile
                    Pool.Clear();
                    foreach (var item in _profile.Pool)
                    {
                        Pool.Add(item);
                    }
                    
                    // Listen to changes on the profile's pool so we update when it's modified externally 
                    _profile.Pool.CollectionChanged += (_, _) => RefreshPool();
                }
            }
        }
    }

    private void RefreshPool()
    {
        if (_profile != null)
        {
            Pool.Clear();
            foreach (var item in _profile.Pool)
            {
                Pool.Add(item);
            }
        }
    }

    public RelayCommand AddAvatarCommand { get; }
    public RelayCommand<RouletteAvatarEntry> RemoveAvatarCommand { get; }
    public RelayCommand AddFromLibraryCommand { get; }
    public RelayCommand ClearPoolCommand { get; }

    public RoulettePoolEditorViewModel()
    {
        AddAvatarCommand = new RelayCommand(AddAvatar);
        RemoveAvatarCommand = new RelayCommand<RouletteAvatarEntry>(RemoveAvatar);
        AddFromLibraryCommand = new RelayCommand(AddFromLibrary);
        ClearPoolCommand = new RelayCommand(ClearPool);
    }

    private void AddAvatar()
    {
        // Placeholder - in a real implementation this would open an avatar selection dialog
        System.Diagnostics.Debug.WriteLine("Add Avatar functionality needs implementation");
        
        // For now, we'll add a dummy entry to demonstrate the UI
        var dummyEntry = new RouletteAvatarEntry 
        { 
            AvatarId = "dummy_avatar_id", 
            AvatarName = "New Avatar" 
        };
        Pool.Add(dummyEntry);
        if (_profile != null)
        {
            _profile.Pool.Add(dummyEntry);
        }
    }

    private void RemoveAvatar(RouletteAvatarEntry? entry)
    {
        if (entry != null && _profile != null)
        {
            Pool.Remove(entry);
            // Also remove from profile's pool 
            _profile.Pool.Remove(entry);
        }
    }

    private void AddFromLibrary()
    {
        // Placeholder - in a real implementation this would open the avatar library
        System.Diagnostics.Debug.WriteLine("Add From Library functionality needs implementation");
        
        // For now, we'll add another dummy entry to demonstrate the UI
        var dummyEntry = new RouletteAvatarEntry 
        { 
            AvatarId = "dummy_avatar_id_2", 
            AvatarName = "Library Avatar" 
        };
        Pool.Add(dummyEntry);
        if (_profile != null)
        {
            _profile.Pool.Add(dummyEntry);
        }
    }

    private void ClearPool()
    {
        if (_profile != null)
        {
            Pool.Clear();
            _profile.Pool.Clear();
        }
    }
}