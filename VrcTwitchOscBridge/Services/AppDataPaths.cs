using System.IO;

namespace VrcTwitchOscBridge.Services;

internal static class AppDataPaths
{
    private static readonly string localAppDataFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string legacyMigrationMarkerPath =
        Path.Combine(Path.Combine(localAppDataFolder, "CrystalRelay"), ".legacy-migrated");

    public static string RootFolder => Path.Combine(localAppDataFolder, "CrystalRelay");

    public static string LegacyRootFolder => Path.Combine(localAppDataFolder, "VrcTwitchOscBridge");

    public static string PortableSaveFolder => Path.Combine(RootFolder, "Crystal Relay Save Transfer");

    public static string ThemeAssetsFolder => Path.Combine(PortableSaveFolder, "ThemeAssets");

    public static string SecureFolder => Path.Combine(RootFolder, "Secure");

    public static string CrashLogFolder => Path.Combine(RootFolder, "CrashLogs");

    public static string RuntimeConfigPath => Path.Combine(RootFolder, "bridge.runtime.json");

    public static void EnsureCoreFolders()
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(PortableSaveFolder);
        Directory.CreateDirectory(ThemeAssetsFolder);
        Directory.CreateDirectory(SecureFolder);
        Directory.CreateDirectory(CrashLogFolder);
    }

    public static void MigrateLegacyRootIfNeeded()
    {
        EnsureCoreFolders();

        if (!Directory.Exists(LegacyRootFolder))
        {
            return;
        }

        if (File.Exists(legacyMigrationMarkerPath))
        {
            return;
        }

        CopyDirectoryContentsIfMissing(LegacyRootFolder, RootFolder);

        try
        {
            File.WriteAllText(legacyMigrationMarkerPath, DateTime.UtcNow.ToString("O"));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void CopyDirectoryContentsIfMissing(string sourceFolder, string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);

        foreach (var sourceFilePath in Directory.GetFiles(sourceFolder))
        {
            var destinationFilePath = Path.Combine(destinationFolder, Path.GetFileName(sourceFilePath));
            if (!File.Exists(destinationFilePath))
            {
                File.Copy(sourceFilePath, destinationFilePath);
            }
        }

        foreach (var sourceDirectoryPath in Directory.GetDirectories(sourceFolder))
        {
            var destinationDirectoryPath = Path.Combine(destinationFolder, Path.GetFileName(sourceDirectoryPath));
            CopyDirectoryContentsIfMissing(sourceDirectoryPath, destinationDirectoryPath);
        }
    }
}
