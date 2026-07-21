using System.Globalization;
using System.IO;

namespace VrcTwitchOscBridge.Services;

public sealed record ApplicationBuildIdentity(
    string BaseVersion,
    ApplicationUpdateChannel Channel,
    string ChannelIdentity,
    string DisplayLabel,
    int BugFixSequence,
    bool IsTestBuild)
{
    public string UpdateVersion => Channel == ApplicationUpdateChannel.Stable
        ? BaseVersion
        : $"{BaseVersion}-{ChannelIdentity}";

    public string BuildChannel => IsTestBuild
        ? "test"
        : Channel switch
        {
            ApplicationUpdateChannel.Stable => "stable",
            ApplicationUpdateChannel.Beta => ChannelIdentity,
            ApplicationUpdateChannel.BugFix => "bugfix",
            _ => "stable"
        };

    public bool HasBetaLabel => Channel == ApplicationUpdateChannel.Beta;
    public bool HasBugFixLabel => Channel == ApplicationUpdateChannel.BugFix;

    public static ApplicationBuildIdentity Detect(string baseVersion, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        try
        {
            if (File.Exists(Path.Combine(baseDirectory, "test-build.flag")))
            {
                return Stable(baseVersion, isTestBuild: true);
            }

            var betaMarkerPath = Path.Combine(baseDirectory, "beta-build.flag");
            if (File.Exists(betaMarkerPath))
            {
                return CreateBeta(baseVersion, File.ReadAllText(betaMarkerPath).Trim());
            }

            var bugFixMarkerPath = Path.Combine(baseDirectory, "bugfix-build.flag");
            if (File.Exists(bugFixMarkerPath))
            {
                var marker = File.ReadAllText(bugFixMarkerPath);
                if (ApplicationUpdatePackageRules.TryParseBugFixVersion(
                    $"{baseVersion}-{marker}",
                    out var parsedBaseVersion,
                    out var sequence)
                    && string.Equals(parsedBaseVersion, baseVersion, StringComparison.Ordinal))
                {
                    return new ApplicationBuildIdentity(
                        baseVersion,
                        ApplicationUpdateChannel.BugFix,
                        $"bugfix{sequence.ToString(CultureInfo.InvariantCulture)}",
                        $"Bug Fix {sequence.ToString(CultureInfo.InvariantCulture)}",
                        sequence,
                        IsTestBuild: false);
                }
            }
        }
        catch
        {
            return Stable(baseVersion, isTestBuild: false);
        }

        return Stable(baseVersion, isTestBuild: false);
    }

    private static ApplicationBuildIdentity CreateBeta(string baseVersion, string marker)
    {
        if (marker.StartsWith("beta", StringComparison.OrdinalIgnoreCase))
        {
            var numberText = marker[4..].Trim(' ', '-', '_');
            if (int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && number > 0)
            {
                return new ApplicationBuildIdentity(
                    baseVersion,
                    ApplicationUpdateChannel.Beta,
                    $"beta{number.ToString(CultureInfo.InvariantCulture)}",
                    $"Beta {number.ToString(CultureInfo.InvariantCulture)}",
                    BugFixSequence: 0,
                    IsTestBuild: false);
            }
        }

        var display = string.IsNullOrWhiteSpace(marker)
            ? "Beta"
            : marker.Length <= 40 ? marker : marker[..40];
        return new ApplicationBuildIdentity(
            baseVersion,
            ApplicationUpdateChannel.Beta,
            "beta",
            display,
            BugFixSequence: 0,
            IsTestBuild: false);
    }

    private static ApplicationBuildIdentity Stable(string baseVersion, bool isTestBuild) =>
        new(
            baseVersion,
            ApplicationUpdateChannel.Stable,
            string.Empty,
            string.Empty,
            BugFixSequence: 0,
            IsTestBuild: isTestBuild);
}
