using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace VrcTwitchOscBridge.Services;

public sealed record SavedLoginRecoveryPreparation(string BackupFolderPath);

public sealed record SavedLoginRecoveryHelperRequest(
    int ParentProcessId,
    string BackupFolderPath,
    string RelaunchExecutablePath);

public sealed record SavedLoginRecoveryResult(
    bool Succeeded,
    string Message,
    string BackupFolderPath,
    string QuarantineFolderPath,
    string ErrorMessage,
    DateTimeOffset CompletedAtUtc);

/// <summary>
/// Backs up portable redeem data, clears broken saved login state, and restores only safe files.
/// </summary>
public static class SavedLoginStateRecoveryService
{
    private const string HelperArgument = "--crystal-relay-recovery-helper";
    private const string ParentProcessIdArgument = "--parent-pid";
    private const string BackupFolderArgument = "--backup-folder";
    private const string RelaunchPathArgument = "--relaunch-path";
    private const string PortableSaveFolderName = "Crystal Relay Save Transfer";
    private const string ThemeAssetsFolderName = "ThemeAssets";
    private const string PortableProfileFileName = "crystal-relay.rules.json";
    private const string PortableProfileBackupFileName = "crystal-relay.rules.json.bak";
    private const string PreservedFolderName = "Preserved";
    private const string ManifestFileName = "recovery-manifest.json";
    private const string RecoveryResultFileName = "saved-login-recovery-result.json";
    private const int ParentExitWaitMilliseconds = 120_000;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static string RecoveryBackupRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CrystalRelay-RecoveryBackups");

    private static string RecoveryResultPath => Path.Combine(AppDataPaths.RootFolder, RecoveryResultFileName);

    public static async Task<SavedLoginRecoveryPreparation> PrepareRecoveryBackupAsync(
        CancellationToken cancellationToken = default)
    {
        AppDataPaths.EnsureCoreFolders();

        var profilePath = Path.Combine(AppDataPaths.PortableSaveFolder, PortableProfileFileName);
        if (!File.Exists(profilePath))
        {
            throw new InvalidOperationException("Redeem save file was not found.");
        }

        await ValidateJsonFileAsync(profilePath, cancellationToken);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var backupFolderPath = Path.Combine(RecoveryBackupRoot, $"saved-login-repair-{timestamp}-{Guid.NewGuid():N}");
        var preservedSaveFolder = Path.Combine(backupFolderPath, PreservedFolderName, PortableSaveFolderName);
        Directory.CreateDirectory(preservedSaveFolder);

        File.Copy(profilePath, Path.Combine(preservedSaveFolder, PortableProfileFileName), overwrite: false);

        var profileBackupPath = Path.Combine(AppDataPaths.PortableSaveFolder, PortableProfileBackupFileName);
        if (File.Exists(profileBackupPath))
        {
            File.Copy(profileBackupPath, Path.Combine(preservedSaveFolder, PortableProfileBackupFileName), overwrite: false);
        }

        if (Directory.Exists(AppDataPaths.ThemeAssetsFolder))
        {
            CopyDirectory(
                AppDataPaths.ThemeAssetsFolder,
                Path.Combine(preservedSaveFolder, ThemeAssetsFolderName));
        }

        var manifest = new RecoveryBackupManifest
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourceAppDataFolder = AppDataPaths.RootFolder,
            IncludedRelativePaths =
            [
                Path.Combine(PortableSaveFolderName, PortableProfileFileName),
                Path.Combine(PortableSaveFolderName, PortableProfileBackupFileName),
                Path.Combine(PortableSaveFolderName, ThemeAssetsFolderName)
            ],
            Excluded = "Secure metadata, caches, crash logs, runtime config, recovery markers, and Windows Credential Manager secrets."
        };

        await File.WriteAllTextAsync(
            Path.Combine(backupFolderPath, ManifestFileName),
            JsonSerializer.Serialize(manifest, SerializerOptions),
            cancellationToken);

        return new SavedLoginRecoveryPreparation(backupFolderPath);
    }

    public static void StartRecoveryHelper(SavedLoginRecoveryPreparation preparation)
    {
        var executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Crystal Relay could not find its executable path.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(HelperArgument);
        startInfo.ArgumentList.Add(ParentProcessIdArgument);
        startInfo.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString());
        startInfo.ArgumentList.Add(BackupFolderArgument);
        startInfo.ArgumentList.Add(preparation.BackupFolderPath);
        startInfo.ArgumentList.Add(RelaunchPathArgument);
        startInfo.ArgumentList.Add(executablePath);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Crystal Relay could not start the recovery helper.");
        }
    }

    public static bool TryCreateHelperRequest(
        IReadOnlyList<string> args,
        out SavedLoginRecoveryHelperRequest request)
    {
        request = new SavedLoginRecoveryHelperRequest(0, string.Empty, string.Empty);
        if (!args.Any(arg => string.Equals(arg, HelperArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var parentProcessId = TryGetIntArgument(args, ParentProcessIdArgument);
        var backupFolderPath = TryGetStringArgument(args, BackupFolderArgument);
        var relaunchPath = TryGetStringArgument(args, RelaunchPathArgument);
        if (parentProcessId <= 0 || string.IsNullOrWhiteSpace(backupFolderPath))
        {
            return false;
        }

        request = new SavedLoginRecoveryHelperRequest(
            parentProcessId,
            backupFolderPath,
            string.IsNullOrWhiteSpace(relaunchPath)
                ? Environment.ProcessPath ?? string.Empty
                : relaunchPath);
        return true;
    }

    public static async Task RunRecoveryHelperAsync(
        SavedLoginRecoveryHelperRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await RunRecoveryAsync(request, cancellationToken);
        TryRelaunchApplication(request.RelaunchExecutablePath);
        Debug.WriteLine(result.Message);
    }

    public static SavedLoginRecoveryResult? TryConsumeRecoveryResult()
    {
        try
        {
            if (!File.Exists(RecoveryResultPath))
            {
                return null;
            }

            var json = File.ReadAllText(RecoveryResultPath);
            var result = JsonSerializer.Deserialize<SavedLoginRecoveryResult>(json, SerializerOptions);
            File.Delete(RecoveryResultPath);
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<SavedLoginRecoveryResult> RunRecoveryAsync(
        SavedLoginRecoveryHelperRequest request,
        CancellationToken cancellationToken)
    {
        var quarantineFolderPath = string.Empty;
        try
        {
            await WaitForParentExitAsync(request.ParentProcessId, cancellationToken);
            ValidatePreparedBackup(request.BackupFolderPath);

            if (Directory.Exists(AppDataPaths.RootFolder))
            {
                quarantineFolderPath = Path.Combine(request.BackupFolderPath, "Quarantined-CrystalRelay");
                MoveDirectoryWithRetry(AppDataPaths.RootFolder, quarantineFolderPath, cancellationToken);
            }

            AppDataPaths.EnsureCoreFolders();
            AppDataPaths.MarkLegacyMigrationComplete();
            RestorePreservedFiles(request.BackupFolderPath);
            ClearSavedCredentialSecrets();

            var result = new SavedLoginRecoveryResult(
                true,
                "Saved login repair restored the preserved redeem files.",
                request.BackupFolderPath,
                quarantineFolderPath,
                string.Empty,
                DateTimeOffset.UtcNow);
            TryWriteRecoveryResult(result);
            return result;
        }
        catch (Exception ex)
        {
            try
            {
                AppDataPaths.EnsureCoreFolders();
                AppDataPaths.MarkLegacyMigrationComplete();
                ClearSavedCredentialSecrets();
            }
            catch
            {
            }

            var result = new SavedLoginRecoveryResult(
                false,
                "Saved login repair could not finish.",
                request.BackupFolderPath,
                quarantineFolderPath,
                ex.Message,
                DateTimeOffset.UtcNow);
            TryWriteRecoveryResult(result);
            return result;
        }
    }

    private static async Task WaitForParentExitAsync(int parentProcessId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(parentProcessId);
            var exitedTask = process.WaitForExitAsync(cancellationToken);
            var completedTask = await Task.WhenAny(exitedTask, Task.Delay(ParentExitWaitMilliseconds, cancellationToken));
            if (completedTask != exitedTask)
            {
                throw new TimeoutException("Crystal Relay did not exit in time for recovery to continue.");
            }

            await exitedTask;
        }
        catch (ArgumentException)
        {
        }
    }

    private static void ValidatePreparedBackup(string backupFolderPath)
    {
        if (string.IsNullOrWhiteSpace(backupFolderPath) || !Directory.Exists(backupFolderPath))
        {
            throw new DirectoryNotFoundException("The recovery backup folder was not found.");
        }

        var preservedProfilePath = Path.Combine(
            backupFolderPath,
            PreservedFolderName,
            PortableSaveFolderName,
            PortableProfileFileName);
        if (!File.Exists(preservedProfilePath))
        {
            throw new FileNotFoundException("The recovery backup does not contain the redeem save file.", preservedProfilePath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(preservedProfilePath));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The recovery backup redeem save file is not a JSON object.");
        }
    }

    private static async Task ValidateJsonFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Redeem save file is not a JSON object.");
        }
    }

    private static void RestorePreservedFiles(string backupFolderPath)
    {
        var preservedSaveFolder = Path.Combine(backupFolderPath, PreservedFolderName, PortableSaveFolderName);
        var targetSaveFolder = AppDataPaths.PortableSaveFolder;
        Directory.CreateDirectory(targetSaveFolder);

        File.Copy(
            Path.Combine(preservedSaveFolder, PortableProfileFileName),
            Path.Combine(targetSaveFolder, PortableProfileFileName),
            overwrite: true);

        var preservedProfileBackupPath = Path.Combine(preservedSaveFolder, PortableProfileBackupFileName);
        if (File.Exists(preservedProfileBackupPath))
        {
            File.Copy(
                preservedProfileBackupPath,
                Path.Combine(targetSaveFolder, PortableProfileBackupFileName),
                overwrite: true);
        }

        var preservedThemeAssetsFolder = Path.Combine(preservedSaveFolder, ThemeAssetsFolderName);
        if (Directory.Exists(preservedThemeAssetsFolder))
        {
            CopyDirectory(preservedThemeAssetsFolder, AppDataPaths.ThemeAssetsFolder);
        }
    }

    private static void MoveDirectoryWithRetry(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var finalDestinationPath = destinationPath;
        if (Directory.Exists(finalDestinationPath))
        {
            finalDestinationPath = $"{destinationPath}-{Guid.NewGuid():N}";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(finalDestinationPath) ?? RecoveryBackupRoot);

        Exception? lastException = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Move(sourcePath, finalDestinationPath);
                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
            }

            Thread.Sleep(500);
        }

        throw new IOException("Crystal Relay app data could not be moved into quarantine.", lastException);
    }

    private static void CopyDirectory(string sourceFolderPath, string destinationFolderPath)
    {
        Directory.CreateDirectory(destinationFolderPath);

        foreach (var sourceFilePath in Directory.GetFiles(sourceFolderPath))
        {
            File.Copy(
                sourceFilePath,
                Path.Combine(destinationFolderPath, Path.GetFileName(sourceFilePath)),
                overwrite: true);
        }

        foreach (var sourceDirectoryPath in Directory.GetDirectories(sourceFolderPath))
        {
            CopyDirectory(
                sourceDirectoryPath,
                Path.Combine(destinationFolderPath, Path.GetFileName(sourceDirectoryPath)));
        }
    }

    private static void ClearSavedCredentialSecrets()
    {
        var credentialStore = new WindowsCredentialStore();
        foreach (var targetName in SettingsStore.SavedSecretCredentialTargets)
        {
            try
            {
                credentialStore.DeleteSecret(targetName);
            }
            catch
            {
            }
        }
    }

    private static void TryWriteRecoveryResult(SavedLoginRecoveryResult result)
    {
        try
        {
            AppDataPaths.EnsureCoreFolders();
            File.WriteAllText(RecoveryResultPath, JsonSerializer.Serialize(result, SerializerOptions));
        }
        catch
        {
        }
    }

    private static void TryRelaunchApplication(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static string TryGetStringArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return string.Empty;
    }

    private static int TryGetIntArgument(IReadOnlyList<string> args, string name)
    {
        return int.TryParse(TryGetStringArgument(args, name), out var value)
            ? value
            : 0;
    }

    private sealed class RecoveryBackupManifest
    {
        public DateTimeOffset CreatedAtUtc { get; set; }

        public string SourceAppDataFolder { get; set; } = string.Empty;

        public List<string> IncludedRelativePaths { get; set; } = [];

        public string Excluded { get; set; } = string.Empty;
    }
}
