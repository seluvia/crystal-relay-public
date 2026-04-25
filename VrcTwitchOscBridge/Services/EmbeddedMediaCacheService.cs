using System.Reflection;
using System.IO;

namespace VrcTwitchOscBridge.Services;

public static class EmbeddedMediaCacheService
{
    public static string? ExtractEmbeddedMediaToTempFile(string relativePath)
    {
        var assembly = typeof(EmbeddedMediaCacheService).Assembly;
        var normalizedSuffix = relativePath.Replace('\\', '.').Replace('/', '.');
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(normalizedSuffix, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return null;
        }

        using var resourceStream = assembly.GetManifestResourceStream(resourceName);
        if (resourceStream is null)
        {
            return null;
        }

        var extension = Path.GetExtension(relativePath);
        var tempDirectory = Path.Combine(Path.GetTempPath(), "CrystalRelay", "media-cache");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}{extension}");

        using var fileStream = File.Create(tempPath);
        resourceStream.CopyTo(fileStream);
        return tempPath;
    }

    public static void DeleteTemporaryMediaFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
