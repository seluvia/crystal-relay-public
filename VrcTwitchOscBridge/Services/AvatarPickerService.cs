using System.Windows;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Opens the AvatarPickerWindow and returns the selected avatar(s).
/// </summary>
public static class AvatarPickerService
{
    private static AvatarImageService? _instance;

    private static AvatarImageService Instance =>
        _instance ??= new AvatarImageService();

    /// <summary>
    /// Opens the avatar picker for single selection.
    /// </summary>
    public static AvatarPickerResult? OpenSingle(
        AppTheme theme,
        IReadOnlyList<VrChatAvatarSummary> avatars,
        AvatarLibrary? avatarLibrary = null,
        string? currentAvatarId = null,
        Window? owner = null)
    {
        var window = new AvatarPickerWindow(
            theme,
            avatars,
            Instance,
            avatarLibrary,
            currentAvatarId);

        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var result = window.ShowDialog();
        if (result != true)
        {
            return null;
        }

        var selectedIds = window.GetSelectedAvatarIds();
        if (selectedIds.Count == 0)
        {
            return null;
        }

        var selectedId = selectedIds[0];
        var selectedAvatar = avatars.FirstOrDefault(a => string.Equals(a.Id, selectedId, StringComparison.Ordinal));
        return new AvatarPickerResult(selectedId, selectedAvatar?.Name ?? selectedId);
    }

    /// <summary>
    /// Opens the avatar picker for multi-selection (Avatar Roulette pool).
    /// </summary>
    public static IReadOnlyList<string> OpenMulti(
        AppTheme theme,
        IReadOnlyList<VrChatAvatarSummary> avatars,
        AvatarLibrary? avatarLibrary = null,
        IReadOnlyList<string>? currentPool = null,
        Window? owner = null)
    {
        var window = new AvatarPickerWindow(
            theme,
            avatars,
            Instance,
            avatarLibrary,
            multiSelectCurrentIds: currentPool);

        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var result = window.ShowDialog();
        return result == true ? window.GetSelectedAvatarIds() : (currentPool ?? []);
    }

    /// <summary>
    /// Clears the image cache. Call when avatar list is refreshed.
    /// </summary>
    public static void ClearImageCache()
    {
        Instance.ClearCache();
    }

    /// <summary>
    /// Sets the VRChat auth cookie for authenticated thumbnail downloads.
    /// Call when VRChat connects (pass cookie) or disconnects (pass null).
    /// </summary>
    public static void SetVrChatAuthCookie(string? cookie)
    {
        Instance.SetVrChatAuthCookie(cookie);
        Instance.ClearCache();
    }
}

public sealed record AvatarPickerResult(string AvatarId, string AvatarName);