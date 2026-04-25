using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class TwitchAccountSettings : ObservableObject
{
    private string accessToken = string.Empty;
    private string refreshToken = string.Empty;
    private string userId = string.Empty;
    private string login = string.Empty;
    private string displayName = string.Empty;
    private string profileImageUrl = string.Empty;
    private DateTimeOffset? accessTokenExpiresAt;
    private DateTimeOffset? sessionRenewalDueAt;
    private List<string> scopes = [];

    public string AccessToken
    {
        get => accessToken;
        set
        {
            if (SetProperty(ref accessToken, value))
            {
                RaisePropertyChanged(nameof(IsConnected));
            }
        }
    }

    public string RefreshToken
    {
        get => refreshToken;
        set => SetProperty(ref refreshToken, value);
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

    public string Login
    {
        get => login;
        set => SetProperty(ref login, value);
    }

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public string ProfileImageUrl
    {
        get => profileImageUrl;
        set => SetProperty(ref profileImageUrl, value);
    }

    public DateTimeOffset? AccessTokenExpiresAt
    {
        get => accessTokenExpiresAt;
        set => SetProperty(ref accessTokenExpiresAt, value);
    }

    public DateTimeOffset? SessionRenewalDueAt
    {
        get => sessionRenewalDueAt;
        set => SetProperty(ref sessionRenewalDueAt, value);
    }

    public List<string> Scopes
    {
        get => scopes;
        set => SetProperty(ref scopes, value ?? []);
    }

    public bool IsConnected => !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(UserId);

    public void Apply(TwitchAccountSettings other)
    {
        AccessToken = other.AccessToken;
        RefreshToken = other.RefreshToken;
        UserId = other.UserId;
        Login = other.Login;
        DisplayName = other.DisplayName;
        ProfileImageUrl = other.ProfileImageUrl;
        AccessTokenExpiresAt = other.AccessTokenExpiresAt;
        SessionRenewalDueAt = other.SessionRenewalDueAt;
        Scopes = [.. other.Scopes];
    }

    public void Clear()
    {
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        UserId = string.Empty;
        Login = string.Empty;
        DisplayName = string.Empty;
        ProfileImageUrl = string.Empty;
        AccessTokenExpiresAt = null;
        SessionRenewalDueAt = null;
        Scopes = [];
    }
}
