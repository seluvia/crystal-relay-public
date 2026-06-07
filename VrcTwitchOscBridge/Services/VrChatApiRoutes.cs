namespace VrcTwitchOscBridge.Services;

internal static class VrChatApiRoutes
{
    public const string CurrentUser = "auth/user";
    public const string Logout = "logout";
    public const string UploadedAvatars = "avatars?user=me&releaseStatus=all";
    public const string FavoriteAvatars = "avatars/favorites";
    public const string LicensedAvatars = "avatars/licensed";

    public static string World(string worldId) => $"worlds/{Uri.EscapeDataString(worldId)}";

    public static string InviteMyselfToInstance(string location) =>
        $"invite/myself/to/{Uri.EscapeDataString(location)}";
}
