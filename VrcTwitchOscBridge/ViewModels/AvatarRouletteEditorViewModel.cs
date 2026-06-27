using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarRouletteEditorViewModel : ObservableObject
{
    private readonly AvatarRouletteProfile _originalProfile;
    
    public AvatarRouletteEditorViewModel(AvatarRouletteProfile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        
        _originalProfile = profile;
        Profile = Clone(profile);
        
        // Initialize collections to ensure they're observable
        Pool = new ObservableCollection<RouletteAvatarEntry>(Profile.Pool);
        Triggers = new ObservableCollection<TriggerRule>(Profile.Triggers);
    }
    
    public AvatarRouletteProfile Profile { get; private set; }
    
    public ObservableCollection<RouletteAvatarEntry> Pool { get; set; }
    
    public ObservableCollection<TriggerRule> Triggers { get; set; }

    public string Name
    {
        get => Profile.Name;
        set
        {
            if (Profile.Name != value)
            {
                Profile.Name = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsDirty));
            }
        }
    }

    public bool IsEnabled
    {
        get => Profile.IsEnabled;
        set
        {
            if (Profile.IsEnabled != value)
            {
                Profile.IsEnabled = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsDirty));
            }
        }
    }

    public bool IsDirty => !IsEqual(_originalProfile, Profile) || 
                         !Pool.SequenceEqual(Profile.Pool) || 
                         !Triggers.SequenceEqual(Profile.Triggers);

    private static bool IsEqual(AvatarRouletteProfile? a, AvatarRouletteProfile? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        
        return a.Id == b.Id &&
               a.Name == b.Name &&
               a.IsEnabled == b.IsEnabled &&
               a.CreatedAt == b.CreatedAt &&
               a.UpdatedAt == b.UpdatedAt;
    }

    public void Save()
    {
        // Update the original profile with changes
        _originalProfile.Name = Name;
        _originalProfile.IsEnabled = IsEnabled;
        
        // Copy pool items to the original profile
        _originalProfile.Pool.Clear();
        foreach (var item in Pool)
        {
            _originalProfile.Pool.Add(item);
        }
        
        // Copy trigger items to the original profile  
        _originalProfile.Triggers.Clear();
        foreach (var item in Triggers)
        {
            _originalProfile.Triggers.Add(item);
        }
        
        // Update timestamps
        _originalProfile.UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        // Revert to original values
        Profile = Clone(_originalProfile);
        Pool.Clear();
        foreach (var item in Profile.Pool)
        {
            Pool.Add(item);
        }
        
        Triggers.Clear();
        foreach (var item in Profile.Triggers)
        {
            Triggers.Add(item);
        }
    }

    private static AvatarRouletteProfile Clone(AvatarRouletteProfile profile)
    {
        return new AvatarRouletteProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            IsEnabled = profile.IsEnabled,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt,
            ReturnAvatarId = profile.ReturnAvatarId,
            ReturnAvatarName = profile.ReturnAvatarName,
            Pool = new ObservableCollection<RouletteAvatarEntry>(profile.Pool),
            Triggers = new ObservableCollection<TriggerRule>(profile.Triggers)
        };
    }
}