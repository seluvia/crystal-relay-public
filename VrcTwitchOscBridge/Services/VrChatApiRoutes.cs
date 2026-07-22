namespace VrcTwitchOscBridge.Services;

internal static class VrChatApiRoutes
{
    public const string CurrentUser = "auth/user";
    public const string Logout = "logout";
    public const string UploadedAvatars = "avatars?user=me&releaseStatus=all";
    public const string FavoriteAvatars = "avatars/favorites";
    public const string LicensedAvatars = "avatars/licensed";

    public const string FavoriteGroups = "favorite/groups";
    public const string AddFavorite = "favorites";
    public const string ListFavorites = "favorites";
    public static string RemoveFavorite(string favoriteId) =>
        $"favorites/{Uri.EscapeDataString(favoriteId)}";

    public static string World(string worldId) => $"worlds/{Uri.EscapeDataString(worldId)}";

    public const string Inventory = "inventory";

    public static string SpawnInventoryItem(string itemId) =>
        $"inventory/spawn?id={Uri.EscapeDataString(itemId)}";

    public static string InviteMyselfToInstance(string location) =>
        $"invite/myself/to/{Uri.EscapeDataString(location)}";
}
