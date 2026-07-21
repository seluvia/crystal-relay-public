using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

public enum ApplicationUpdateChannel
{
    Stable,
    Beta,
    BugFix
}

public static class ApplicationUpdatePackageRules
{
    private const string RuntimeName = "win-x64";
    private const string StablePackagePrefix = "CrystalRelayTwitchOsc-v";
    private const string BugFixPackagePrefix = "CrystalRelayBugFix-v";
    private const string LegacyPackagePrefix = "CrystalRelay-v";
    private const string StaticExecutableName = "Crystal Relay.exe";
    private const string VersionedExecutablePrefix = "CrystalRelayTwitchOsc-v";
    private static readonly Regex BugFixVersionPattern = new(
        "^(?<base>\\d+\\.\\d+\\.\\d+)-bugfix(?<sequence>[1-9]\\d*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string GetManifestChannel(ApplicationUpdateChannel channel) => channel switch
    {
        ApplicationUpdateChannel.Stable => "stable",
        ApplicationUpdateChannel.Beta => "beta",
        ApplicationUpdateChannel.BugFix => "bugfix",
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    public static bool TryParseManifestChannel(string? value, out ApplicationUpdateChannel channel)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "stable":
                channel = ApplicationUpdateChannel.Stable;
                return true;
            case "beta":
                channel = ApplicationUpdateChannel.Beta;
                return true;
            case "bugfix":
                channel = ApplicationUpdateChannel.BugFix;
                return true;
            default:
                channel = default;
                return false;
        }
    }

    public static string GetExpectedAssetName(ApplicationUpdateChannel channel, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var prefix = channel switch
        {
            ApplicationUpdateChannel.Stable or ApplicationUpdateChannel.Beta => StablePackagePrefix,
            ApplicationUpdateChannel.BugFix => BugFixPackagePrefix,
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
        return $"{prefix}{version}-{RuntimeName}.zip";
    }

    public static bool TryParseBugFixVersion(
        string? value,
        out string baseVersion,
        out int sequence)
    {
        baseVersion = string.Empty;
        sequence = 0;
        var match = BugFixVersionPattern.Match(value ?? string.Empty);
        if (!match.Success
            || !int.TryParse(
                match.Groups["sequence"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sequence))
        {
            sequence = 0;
            return false;
        }

        baseVersion = match.Groups["base"].Value;
        return true;
    }

    public static string GetExpectedEntryExecutableName(
        ApplicationUpdateChannel channel,
        string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return channel switch
        {
            ApplicationUpdateChannel.Stable or ApplicationUpdateChannel.Beta => StaticExecutableName,
            ApplicationUpdateChannel.BugFix => $"{VersionedExecutablePrefix}{version}.exe",
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }

    public static bool IsExpectedEntryExecutableName(
        ApplicationUpdateChannel channel,
        string version,
        string? fileName)
    {
        if (channel == ApplicationUpdateChannel.BugFix)
        {
            return string.Equals(
                fileName,
                GetExpectedEntryExecutableName(channel, version),
                StringComparison.Ordinal);
        }

        return string.Equals(fileName, StaticExecutableName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                fileName,
                $"{VersionedExecutablePrefix}{version}.exe",
                StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetExpectedBuildMarker(
        ApplicationUpdateChannel channel,
        string version) =>
        channel == ApplicationUpdateChannel.BugFix
        && TryParseBugFixVersion(version, out _, out var sequence)
            ? $"bugfix{sequence.ToString(CultureInfo.InvariantCulture)}"
            : null;

    public static bool IsExpectedBuildMarker(
        ApplicationUpdateChannel channel,
        string version,
        string? markerText)
    {
        var expected = GetExpectedBuildMarker(channel, version);
        return expected is null
            || string.Equals(markerText, expected, StringComparison.Ordinal);
    }

    public static bool IsApplicationExecutableName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && (string.Equals(fileName, StaticExecutableName, StringComparison.OrdinalIgnoreCase)
            || (fileName.StartsWith(VersionedExecutablePrefix, StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)));

    public static bool ShouldRelocateInstallFolder(ApplicationUpdateChannel channel) => channel switch
    {
        ApplicationUpdateChannel.Stable or ApplicationUpdateChannel.Beta => true,
        ApplicationUpdateChannel.BugFix => false,
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    public static string GetInstallTargetDirectory(
        ApplicationUpdateChannel channel,
        string sourceDirectory,
        string packageRoot)
    {
        if (!ShouldRelocateInstallFolder(channel))
        {
            return sourceDirectory;
        }

        var sourceFolderName = Path.GetFileName(sourceDirectory);
        var packageFolderName = Path.GetFileName(packageRoot);
        if (!IsPackageInstallFolderName(sourceFolderName)
            || !IsPackageInstallFolderName(packageFolderName))
        {
            return sourceDirectory;
        }

        var sourceParent = Path.GetDirectoryName(sourceDirectory);
        return string.IsNullOrWhiteSpace(sourceParent)
            ? sourceDirectory
            : Path.GetFullPath(Path.Combine(sourceParent, packageFolderName));
    }

    public static bool IsPackageInstallFolderName(string? folderName) =>
        !string.IsNullOrWhiteSpace(folderName)
        && !folderName.Contains(Path.DirectorySeparatorChar)
        && !folderName.Contains(Path.AltDirectorySeparatorChar)
        && (string.Equals(folderName, "Crystal Relay", StringComparison.OrdinalIgnoreCase)
            || HasPackageShape(folderName, StablePackagePrefix)
            || HasPackageShape(folderName, BugFixPackagePrefix)
            || HasPackageShape(folderName, LegacyPackagePrefix));

    private static bool HasPackageShape(string folderName, string prefix) =>
        folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && folderName.EndsWith($"-{RuntimeName}", StringComparison.OrdinalIgnoreCase);
}
