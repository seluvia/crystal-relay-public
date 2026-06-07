using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Resolves avatar images from custom local icons, VRChat API thumbnails, or a built-in placeholder.
/// Images are cached locally to avoid repeated downloads.
/// </summary>
public sealed class AvatarImageService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelayTwitchOsc/desktop");
        return client;
    }

    private readonly string iconFolder;
    private readonly string cacheFolder;
    private readonly ConcurrentDictionary<string, ImageSource> imageCache = new(StringComparer.Ordinal);

    private string? vrChatAuthCookie;

    /// <summary>
    /// Sets the VRChat auth cookie used for authenticated thumbnail downloads.
    /// Call when VRChat connects (pass the cookie) or disconnects (pass null).
    /// </summary>
    public void SetVrChatAuthCookie(string? cookie)
    {
        vrChatAuthCookie = string.IsNullOrWhiteSpace(cookie) ? null : cookie.Trim();
    }

    public AvatarImageService()
    {
        var themeAssets = AppDataPaths.ThemeAssetsFolder ?? string.Empty;
        iconFolder = string.IsNullOrWhiteSpace(themeAssets)
            ? string.Empty
            : Path.Combine(themeAssets, "AvatarIcons");
        cacheFolder = string.IsNullOrWhiteSpace(iconFolder)
            ? string.Empty
            : Path.Combine(iconFolder, "Cache");

        if (!string.IsNullOrWhiteSpace(iconFolder) && !Directory.Exists(iconFolder))
        {
            Directory.CreateDirectory(iconFolder);
        }
        if (!string.IsNullOrWhiteSpace(cacheFolder) && !Directory.Exists(cacheFolder))
        {
            Directory.CreateDirectory(cacheFolder);
        }
    }

    /// <summary>
    /// Gets the image source for an avatar. Tries custom icon first, then VRChat thumbnail, then placeholder.
    /// </summary>
    public ImageSource? GetAvatarImage(
        string avatarId,
        string? customIconPath,
        string? vrchatThumbnailUrl)
    {
        var cacheKey = BuildMemoryCacheKey(avatarId, customIconPath, vrchatThumbnailUrl);
        if (imageCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var image = LoadCustomIcon(customIconPath)
            ?? LoadVrChatThumbnail(avatarId, vrchatThumbnailUrl)
            ?? GetPlaceholderImage();

        imageCache[cacheKey] = image;
        return image;
    }

    /// <summary>
    /// Gets the custom icon for an avatar synchronously, without loading VRChat thumbnails.
    /// Returns null if no custom icon is available.
    /// </summary>
    public ImageSource? GetCustomIconOnly(string? customIconPath)
    {
        return LoadCustomIcon(customIconPath);
    }

    /// <summary>
    /// Gets the image source for an avatar asynchronously. Tries custom icon first, then VRChat thumbnail, then placeholder.
    /// </summary>
    public async Task<ImageSource?> GetAvatarImageAsync(
        string avatarId,
        string? customIconPath,
        string? vrchatThumbnailUrl,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildMemoryCacheKey(avatarId, customIconPath, vrchatThumbnailUrl);
        if (imageCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var image = LoadCustomIcon(customIconPath)
            ?? await LoadVrChatThumbnailAsync(avatarId, vrchatThumbnailUrl, cancellationToken)
            ?? GetPlaceholderImage();

        imageCache[cacheKey] = image;
        return image;
    }

    private string BuildMemoryCacheKey(string avatarId, string? customIconPath, string? vrchatThumbnailUrl)
    {
        var customIconKey = ResolveCustomIconPath(customIconPath) ?? string.Empty;
        var thumbnailKey = string.IsNullOrWhiteSpace(vrchatThumbnailUrl) ? string.Empty : vrchatThumbnailUrl.Trim();
        return string.Join("|", avatarId.Trim(), customIconKey, thumbnailKey);
    }

    /// <summary>
    /// Clears the in-memory image cache. Called when avatar list is refreshed.
    /// </summary>
    public void ClearCache()
    {
        imageCache.Clear();
    }

    /// <summary>
    /// Deletes all cached thumbnail files from disk so they are re-downloaded on next load.
    /// </summary>
    public void ClearDiskCache()
    {
        if (string.IsNullOrWhiteSpace(cacheFolder) || !Directory.Exists(cacheFolder))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(cacheFolder))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // File may be locked; skip and continue
            }
        }
    }

    /// <summary>
    /// Saves a custom icon file for an avatar and returns the relative path.
    /// </summary>
    public string? SaveCustomIcon(string avatarId, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(iconFolder) || !File.Exists(sourceFilePath))
        {
            return null;
        }

        var extension = Path.GetExtension(sourceFilePath);
        var fileName = $"{avatarId}{extension}";
        var destPath = Path.Combine(iconFolder, fileName);

        File.Copy(sourceFilePath, destPath, overwrite: true);
        return Path.Combine("AvatarIcons", fileName);
    }

    /// <summary>
    /// Gets the full path to a custom icon from its relative path.
    /// </summary>
    public string? ResolveCustomIconPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(iconFolder))
        {
            return null;
        }

        var fullPath = Path.Combine(iconFolder, Path.GetFileName(relativePath));
        return File.Exists(fullPath) ? fullPath : null;
    }

    private ImageSource? LoadCustomIcon(string? customIconPath)
    {
        var fullPath = ResolveCustomIconPath(customIconPath);
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(fullPath);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private ImageSource? LoadVrChatThumbnail(string avatarId, string? thumbnailUrl)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl) || string.IsNullOrWhiteSpace(cacheFolder))
        {
            return null;
        }

        var cachePath = Path.Combine(cacheFolder, $"{avatarId}.jpg");
        if (File.Exists(cachePath))
        {
            return LoadImageFromFile(cachePath);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, thumbnailUrl);
            if (!string.IsNullOrWhiteSpace(vrChatAuthCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", $"auth={vrChatAuthCookie}");
            }

            using var response = HttpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            File.WriteAllBytes(cachePath, bytes);
            return LoadImageFromFile(cachePath);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ImageSource?> LoadVrChatThumbnailAsync(string avatarId, string? thumbnailUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl) || string.IsNullOrWhiteSpace(cacheFolder))
        {
            return null;
        }

        var cachePath = Path.Combine(cacheFolder, $"{avatarId}.jpg");
        if (File.Exists(cachePath))
        {
            return LoadImageFromFile(cachePath);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, thumbnailUrl);
            if (!string.IsNullOrWhiteSpace(vrChatAuthCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", $"auth={vrChatAuthCookie}");
            }

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[AvatarImageService] HTTP {(int)response.StatusCode} for {thumbnailUrl}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            File.WriteAllBytes(cachePath, bytes);
            return LoadImageFromFile(cachePath);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadImageFromFile(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the built-in placeholder image used when no custom icon or thumbnail is available.
    /// </summary>
    public ImageSource GetPlaceholderImage() => GetPlaceholderImageCore();

    private static ImageSource GetPlaceholderImageCore()
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(40, 25, 60)),
                new Pen(new SolidColorBrush(Color.FromRgb(80, 50, 120)), 1),
                new Rect(0, 0, 120, 120));

            context.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(120, 90, 160)),
                null,
                new Point(60, 45), 20, 20);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(120, 90, 160)),
                null,
                new Point(60, 95), 35, 25);
        }

        drawing.Freeze();
        var visual = new DrawingImage(drawing);
        visual.Freeze();
        return visual;
    }
}
