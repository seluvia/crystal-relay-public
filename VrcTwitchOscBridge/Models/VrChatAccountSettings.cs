using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class VrChatAccountSettings : ObservableObject
{
    private string authCookie = string.Empty;
    private string userId = string.Empty;
    private string displayName = string.Empty;
    private string currentAvatarId = string.Empty;

    public string AuthCookie
    {
        get => authCookie;
        set
        {
            if (SetProperty(ref authCookie, value))
            {
                RaisePropertyChanged(nameof(IsConnected));
            }
        }
    }

    public string UserId
    {
        get => userId;
        set
        {
            if (SetProperty(ref userId, value))
            {
                RaisePropertyChanged(nameof(IsConnected));
            }
        }
    }

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public string CurrentAvatarId
    {
        get => currentAvatarId;
        set => SetProperty(ref currentAvatarId, value);
    }

    public bool IsConnected => !string.IsNullOrWhiteSpace(AuthCookie) && !string.IsNullOrWhiteSpace(UserId);

    public void Apply(VrChatAccountSettings other)
    {
        AuthCookie = other.AuthCookie;
        UserId = other.UserId;
        DisplayName = other.DisplayName;
        CurrentAvatarId = other.CurrentAvatarId;
    }

    public void Clear()
    {
        AuthCookie = string.Empty;
        UserId = string.Empty;
        DisplayName = string.Empty;
        CurrentAvatarId = string.Empty;
    }
}
