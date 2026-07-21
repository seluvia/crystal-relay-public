# Crystal Relay Bug Fix Push Channel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a mandatory, installable GitHub Bug Fix Push channel for exact stable versions while preserving Crystal Relay's existing stable, beta, and self-update behavior.

**Architecture:** Introduce one shared channel/package policy source file compiled into both the WPF app and dedicated updater. Detect the installed build identity from stable, beta, test, or `bugfix-build.flag` markers; classify GitHub releases into stable, beta, and exact `bugfixN` candidates; select candidates with stable > Bug Fix > beta precedence; and reuse the existing full-package updater with Bug Fix installs forced to replace the current installation directory in place. Extend the existing themed dialog with a dedicated scrollable Bug Fix mode and add a standalone PowerShell build/publish lane.

**Tech Stack:** C# 14, WPF/XAML, .NET 10 (`net10.0-windows`), xUnit 2.9, PowerShell 5.1, GitHub CLI (`gh`), GitHub Releases API.

## Global Constraints

- Current stable release is `v3.1.9`; Bug Fix Push support first ships in normal stable `v3.1.10`.
- Update checks remain startup-only and use one GitHub API request with `per_page=100`.
- Exact Bug Fix identity is `v<base>-bugfix<N>` with a positive integer sequence.
- Exact asset name is `CrystalRelayBugFix-v<base>-bugfix<N>-win-x64.zip`.
- Exact manifest values are `version: <base>-bugfix<N>` and `channel: bugfix`.
- Exact marker is `bugfix-build.flag` containing `bugfix<N>`.
- Bug Fix Pushes target one exact stable base, are cumulative, and never target beta/test builds.
- A newer eligible stable wins; otherwise Bug Fix wins over optional beta.
- Stable/beta ignore settings never suppress an eligible Bug Fix Push.
- `Later` defers only until the next launch; no persistent ignore control or setting is added.
- Bug Fix packages are complete packages, not deltas, and use the existing HTTPS, SHA-256, ZIP safety, backup, replacement, relaunch, and cleanup path.
- Bug Fix apply plans always clear and replace the current install directory instead of relocating it.
- Older clients ignore Bug Fix releases through the distinct `CrystalRelayBugFix-` asset prefix.
- GitHub Bug Fix releases are normal releases, not prereleases, and do not change the website's pinned stable URL.
- `Build-Crystal-Relay-BugFix.ps1` and `BugFixBuildScriptTests.cs` are private maintainer tooling because they contain internal repository/publication gates; public export and preflight must exclude/block them rather than weaken content checks.
- Keep the project base version at `3.1.10`; the marker, package, manifest, and executable carry the Bug Fix identity.
- Preserve unrelated dirty-worktree changes. Do not revert, rewrite, stage, or commit them.
- Do not commit, tag, push, publish, or create a GitHub release unless the user explicitly authorizes that exact operation.
- Any commit commands below are gated checkpoints and must be skipped without explicit commit authorization.

## File Structure

### New files

- `VrcTwitchOscBridge/Services/ApplicationUpdatePackageRules.cs`: shared channel enum, channel parsing, asset naming, package-folder allowlist, and install-target policy; linked into the updater project.
- `VrcTwitchOscBridge/Services/ApplicationBuildIdentity.cs`: deterministic stable/beta/test/Bug Fix marker detection and installed identity formatting.
- `VrcTwitchOscBridge.Tests/ApplicationUpdatePackageRulesTests.cs`: pure package-policy coverage.
- `VrcTwitchOscBridge.Tests/ApplicationBuildIdentityTests.cs`: marker precedence and identity coverage.
- `VrcTwitchOscBridge.Tests/ApplicationUpdateServiceTests.cs`: mocked GitHub release discovery and precedence coverage.
- `VrcTwitchOscBridge.Tests/ApplicationSelfUpdateBugFixTests.cs`: package validation and updater parity coverage.
- `VrcTwitchOscBridge.Tests/BugFixUpdateUiTests.cs`: source/XAML regression coverage for dialog behavior and build badge.
- `VrcTwitchOscBridge.Tests/BugFixBuildScriptTests.cs`: source contract for the build/publish script.
- `Build-Crystal-Relay-BugFix.ps1`: guarded package builder and optional GitHub publisher.

### Modified files

- `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`: move active source to `3.1.10`; explicitly include new app source files.
- `CrystalRelayUpdater/CrystalRelayUpdater.csproj`: move updater to `3.1.10`; link the shared package-rules source.
- `VrcTwitchOscBridge/Services/ApplicationUpdateService.cs`: channel-aware discovery, release body, exact Bug Fix parsing, and selection.
- `VrcTwitchOscBridge/Services/ApplicationSelfUpdateService.cs`: channel policy, Bug Fix package validation, and in-place apply target.
- `CrystalRelayUpdater/Program.cs`: validate shared channels and use the same in-place target policy.
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`: consume `ApplicationBuildIdentity` and expose Bug Fix badge state.
- `VrcTwitchOscBridge/MainWindow.xaml`: show `BUG FIX <N>` beside the base version.
- `VrcTwitchOscBridge/MainWindow.xaml.cs`: dedicated Bug Fix startup dialog branch with no ignore action.
- `VrcTwitchOscBridge/ThemedDialogWindow.xaml`: optional scrollable release-details area and non-closing GitHub link.
- `VrcTwitchOscBridge/ThemedDialogWindow.xaml.cs`: `ShowBugFixUpdate` API and details-link behavior.
- `VrcTwitchOscBridge/Resources/Localization/*.extra.json`: localized Bug Fix dialog/build strings for every supported language.
- `tools/github/Export-Crystal-Relay-Public.ps1`: exclude the private maintainer Bug Fix build/publish script and its contract test from public export.
- `tools/github/Test-Crystal-Relay-PublicSafety.ps1`: block accidental copies of the private maintainer Bug Fix script/test in public candidates.
- `AGENTS.md`: full Bug Fix lane, naming, source-isolation, changelog, build, and publication rules.
- `RELEASE-CHANGE-RECORD.txt`: record the new update channel in the `v3.1.10` working draft.

---

### Task 1: Align The Active Source Version

**Files:**
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj:14-17`
- Modify: `CrystalRelayUpdater/CrystalRelayUpdater.csproj:9-12`
- Modify: `RELEASE-CHANGE-RECORD.txt:20-25`

**Interfaces:**
- Consumes: current stable `3.1.9` and approved next source version `3.1.10`.
- Produces: both executable projects reporting base version `3.1.10`; release record no longer claiming source files are `3.1.9`.

- [ ] **Step 1: Verify the existing mismatch**

Run:

```powershell
dotnet msbuild "VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" -getProperty:Version
dotnet msbuild "CrystalRelayUpdater\CrystalRelayUpdater.csproj" -getProperty:Version
```

Expected before the change: both commands report `3.1.9`, while `AGENTS.md` and the release record identify `3.1.10` as current working source.

- [ ] **Step 2: Set both project versions to `3.1.10`**

Use these exact property values in both project files:

```xml
<Version>3.1.10</Version>
<AssemblyVersion>3.1.10.0</AssemblyVersion>
<FileVersion>3.1.10.0</FileVersion>
<InformationalVersion>3.1.10</InformationalVersion>
```

Replace the release-record source note with:

```text
- Source files and project versions now reflect v3.1.10 active development.
```

- [ ] **Step 3: Verify the baseline**

Run the two `dotnet msbuild -getProperty:Version` commands again.

Expected: both report `3.1.10`.

- [ ] **Step 4: Build both projects**

Run:

```powershell
dotnet build "VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
dotnet build "CrystalRelayUpdater\CrystalRelayUpdater.csproj" --no-restore
```

Expected: both builds succeed.

- [ ] **Step 5: Commit checkpoint only when explicitly authorized**

```powershell
git add -- "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" "CrystalRelayUpdater/CrystalRelayUpdater.csproj" "RELEASE-CHANGE-RECORD.txt"
git commit -m "chore: begin v3.1.10 development"
```

Without explicit authorization, skip these commands and retain the uncommitted diff.

---

### Task 2: Add Shared Update Channel And Package Rules

**Files:**
- Create: `VrcTwitchOscBridge/Services/ApplicationUpdatePackageRules.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj:234-240`
- Modify: `CrystalRelayUpdater/CrystalRelayUpdater.csproj:18`
- Test: `VrcTwitchOscBridge.Tests/ApplicationUpdatePackageRulesTests.cs`

**Interfaces:**
- Consumes: package runtime `win-x64`, existing stable/beta package names, the static `Crystal Relay.exe` compatibility name, and the approved versioned Bug Fix executable name.
- Produces: `ApplicationUpdateChannel`, exact Bug Fix identity parsing, channel/asset/entry/marker rules, application-executable recognition, package-folder recognition, and the shared in-place install-target policy used by all later tasks.

- [ ] **Step 1: Write the failing package-policy tests**

Create `VrcTwitchOscBridge.Tests/ApplicationUpdatePackageRulesTests.cs`:

```csharp
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ApplicationUpdatePackageRulesTests
{
    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, "3.1.10", "CrystalRelayTwitchOsc-v3.1.10-win-x64.zip")]
    [InlineData(ApplicationUpdateChannel.Beta, "3.1.11-beta1", "CrystalRelayTwitchOsc-v3.1.11-beta1-win-x64.zip")]
    [InlineData(ApplicationUpdateChannel.BugFix, "3.1.10-bugfix2", "CrystalRelayBugFix-v3.1.10-bugfix2-win-x64.zip")]
    public void GetExpectedAssetName_UsesChannelSpecificPattern(
        ApplicationUpdateChannel channel,
        string version,
        string expected)
    {
        Assert.Equal(expected, ApplicationUpdatePackageRules.GetExpectedAssetName(channel, version));
    }

    [Theory]
    [InlineData("3.1.10-bugfix1", true, "3.1.10", 1)]
    [InlineData("3.1.10-bugfix27", true, "3.1.10", 27)]
    [InlineData("v3.1.10-bugfix1", false, "", 0)]
    [InlineData("3.1.10-bugfix", false, "", 0)]
    [InlineData("3.1.10-bugfix0", false, "", 0)]
    [InlineData("3.1.10-bugfix01", false, "", 0)]
    [InlineData("3.1.10-BugFix1", false, "", 0)]
    [InlineData(" 3.1.10-bugfix1", false, "", 0)]
    [InlineData("3.1.10-bugfix1 ", false, "", 0)]
    [InlineData("3.1.10-bugfix1-extra", false, "", 0)]
    public void TryParseBugFixVersion_RequiresExactManifestIdentity(
        string value,
        bool expected,
        string expectedBase,
        int expectedSequence)
    {
        var parsed = ApplicationUpdatePackageRules.TryParseBugFixVersion(
            value,
            out var baseVersion,
            out var sequence);

        Assert.Equal(expected, parsed);
        Assert.Equal(expectedBase, baseVersion);
        Assert.Equal(expectedSequence, sequence);
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, "stable")]
    [InlineData(ApplicationUpdateChannel.Beta, "beta")]
    [InlineData(ApplicationUpdateChannel.BugFix, "bugfix")]
    public void ManifestChannel_RoundTrips(ApplicationUpdateChannel channel, string text)
    {
        Assert.Equal(text, ApplicationUpdatePackageRules.GetManifestChannel(channel));
        Assert.True(ApplicationUpdatePackageRules.TryParseManifestChannel(text, out var parsed));
        Assert.Equal(channel, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("test")]
    [InlineData("hotfix")]
    [InlineData("stable-beta")]
    public void TryParseManifestChannel_RejectsUnsupportedValues(string value)
    {
        Assert.False(ApplicationUpdatePackageRules.TryParseManifestChannel(value, out _));
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, true)]
    [InlineData(ApplicationUpdateChannel.Beta, true)]
    [InlineData(ApplicationUpdateChannel.BugFix, false)]
    public void ShouldRelocateInstallFolder_KeepsBugFixInPlace(
        ApplicationUpdateChannel channel,
        bool expected)
    {
        Assert.Equal(expected, ApplicationUpdatePackageRules.ShouldRelocateInstallFolder(channel));
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, "3.1.10", "Crystal Relay.exe")]
    [InlineData(ApplicationUpdateChannel.Beta, "3.1.11-beta1", "Crystal Relay.exe")]
    [InlineData(ApplicationUpdateChannel.BugFix, "3.1.10-bugfix2", "CrystalRelayTwitchOsc-v3.1.10-bugfix2.exe")]
    public void GetExpectedEntryExecutableName_PreservesStableAndVersionsBugFix(
        ApplicationUpdateChannel channel,
        string version,
        string expected)
    {
        Assert.Equal(
            expected,
            ApplicationUpdatePackageRules.GetExpectedEntryExecutableName(channel, version));
    }

    [Theory]
    [InlineData("Crystal Relay.exe", true)]
    [InlineData("CrystalRelayTwitchOsc-v3.1.10-bugfix1.exe", true)]
    [InlineData("CrystalRelayUpdater.exe", false)]
    [InlineData("CrystalRelayTwitchOsc-v3.1.10-bugfix1.dll", false)]
    public void IsApplicationExecutableName_AcceptsStaticAndVersionedAppNames(
        string fileName,
        bool expected)
    {
        Assert.Equal(expected, ApplicationUpdatePackageRules.IsApplicationExecutableName(fileName));
    }

    [Fact]
    public void BugFixEntryAndMarker_MustMatchExactSequence()
    {
        Assert.True(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.BugFix,
            "3.1.10-bugfix2",
            "CrystalRelayTwitchOsc-v3.1.10-bugfix2.exe"));
        Assert.False(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.BugFix,
            "3.1.10-bugfix2",
            "CrystalRelayTwitchOsc-v3.1.10-bugfix1.exe"));
        Assert.True(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.Stable,
            "3.1.10",
            "Crystal Relay.exe"));
        Assert.True(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.Stable,
            "3.1.10",
            "CrystalRelayTwitchOsc-v3.1.10.exe"));
        Assert.False(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.Stable,
            "3.1.10",
            "CrystalRelayTwitchOsc-v3.1.9.exe"));
        Assert.Equal(
            "bugfix2",
            ApplicationUpdatePackageRules.GetExpectedBuildMarker(
                ApplicationUpdateChannel.BugFix,
                "3.1.10-bugfix2"));
        Assert.True(ApplicationUpdatePackageRules.IsExpectedBuildMarker(
            ApplicationUpdateChannel.BugFix,
            "3.1.10-bugfix2",
            "bugfix2"));
        Assert.False(ApplicationUpdatePackageRules.IsExpectedBuildMarker(
            ApplicationUpdateChannel.BugFix,
            "3.1.10-bugfix2",
            "bugfix1"));
    }

    [Theory]
    [InlineData("Crystal Relay")]
    [InlineData("CrystalRelayTwitchOsc-v3.1.10-win-x64")]
    [InlineData("CrystalRelayBugFix-v3.1.10-bugfix1-win-x64")]
    [InlineData("CrystalRelay-v3.1.9-win-x64")]
    public void IsPackageInstallFolderName_AcceptsSupportedShapes(string folderName)
    {
        Assert.True(ApplicationUpdatePackageRules.IsPackageInstallFolderName(folderName));
    }

    [Fact]
    public void GetInstallTargetDirectory_ReturnsCurrentDirectoryForBugFix()
    {
        var source = Path.GetFullPath(Path.Combine("C:\\", "Apps", "Crystal Relay"));
        var package = Path.GetFullPath(Path.Combine("C:\\", "Staging", "CrystalRelayBugFix-v3.1.10-bugfix1-win-x64"));

        var target = ApplicationUpdatePackageRules.GetInstallTargetDirectory(
            ApplicationUpdateChannel.BugFix,
            source,
            package);

        Assert.Equal(source, target, ignoreCase: true);
    }

    [Fact]
    public void GetInstallTargetDirectory_PreservesExistingStableRelocation()
    {
        var source = Path.GetFullPath(Path.Combine("C:\\", "Apps", "CrystalRelayTwitchOsc-v3.1.9-win-x64"));
        var package = Path.GetFullPath(Path.Combine("C:\\", "Staging", "CrystalRelayTwitchOsc-v3.1.10-win-x64"));

        var target = ApplicationUpdatePackageRules.GetInstallTargetDirectory(
            ApplicationUpdateChannel.Stable,
            source,
            package);

        Assert.Equal(
            Path.GetFullPath(Path.Combine("C:\\", "Apps", "CrystalRelayTwitchOsc-v3.1.10-win-x64")),
            target,
            ignoreCase: true);
    }
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run:

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ApplicationUpdatePackageRulesTests"
```

Expected: build failure because `ApplicationUpdateChannel` and `ApplicationUpdatePackageRules` do not exist.

- [ ] **Step 3: Implement the shared rules**

Create `VrcTwitchOscBridge/Services/ApplicationUpdatePackageRules.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

internal enum ApplicationUpdateChannel
{
    Stable,
    Beta,
    BugFix
}

internal static class ApplicationUpdatePackageRules
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
```

Add to the app project beside the existing update services:

```xml
<Compile Include="Services\ApplicationUpdatePackageRules.cs" />
```

Add to `CrystalRelayUpdater.csproj` after the property group:

```xml
<ItemGroup>
  <Compile Include="..\VrcTwitchOscBridge\Services\ApplicationUpdatePackageRules.cs"
           Link="ApplicationUpdatePackageRules.cs" />
</ItemGroup>
```

- [ ] **Step 4: Run the package-policy tests and verify GREEN**

Run the Task 2 test command.

Expected: all `ApplicationUpdatePackageRulesTests` pass.

- [ ] **Step 5: Build the linked updater source**

Run:

```powershell
dotnet build "CrystalRelayUpdater\CrystalRelayUpdater.csproj" --no-restore
```

Expected: build succeeds with the linked source compiled into the updater.

- [ ] **Step 6: Commit checkpoint only when explicitly authorized**

```powershell
git add -- "VrcTwitchOscBridge/Services/ApplicationUpdatePackageRules.cs" "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" "CrystalRelayUpdater/CrystalRelayUpdater.csproj" "VrcTwitchOscBridge.Tests/ApplicationUpdatePackageRulesTests.cs"
git commit -m "feat(update): add shared update channel rules"
```

Without explicit authorization, skip these commands.

---

### Task 3: Detect Stable, Beta, Test, And Bug Fix Build Identity

**Files:**
- Create: `VrcTwitchOscBridge/Services/ApplicationBuildIdentity.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs:80-153, 1347-1365, 20364-20468`
- Modify: `VrcTwitchOscBridge/MainWindow.xaml:1809-1827`
- Test: `VrcTwitchOscBridge.Tests/ApplicationBuildIdentityTests.cs`
- Test: `VrcTwitchOscBridge.Tests/BugFixUpdateUiTests.cs`

**Interfaces:**
- Consumes: `ApplicationUpdateChannel` from Task 2 and marker files in `AppContext.BaseDirectory`.
- Produces: `ApplicationBuildIdentity.Detect`, `UpdateVersion`, `BuildChannel`, `DisplayLabel`, `BugFixSequence`, `HasBetaLabel`, and `HasBugFixLabel` for update discovery and UI.

- [ ] **Step 1: Write failing marker-identity tests**

Create `VrcTwitchOscBridge.Tests/ApplicationBuildIdentityTests.cs`:

```csharp
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ApplicationBuildIdentityTests
{
    [Fact]
    public void Detect_NoMarkers_ReturnsStableIdentity()
    {
        using var folder = TemporaryFolder.Create();
        var identity = ApplicationBuildIdentity.Detect("3.1.10", folder.Path);

        Assert.Equal(ApplicationUpdateChannel.Stable, identity.Channel);
        Assert.Equal("3.1.10", identity.UpdateVersion);
        Assert.Equal("stable", identity.BuildChannel);
        Assert.False(identity.IsTestBuild);
    }

    [Fact]
    public void Detect_BugFixMarker_ReturnsExactSequence()
    {
        using var folder = TemporaryFolder.Create();
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix2");

        var identity = ApplicationBuildIdentity.Detect("3.1.10", folder.Path);

        Assert.Equal(ApplicationUpdateChannel.BugFix, identity.Channel);
        Assert.Equal("bugfix2", identity.ChannelIdentity);
        Assert.Equal("Bug Fix 2", identity.DisplayLabel);
        Assert.Equal(2, identity.BugFixSequence);
        Assert.Equal("3.1.10-bugfix2", identity.UpdateVersion);
        Assert.Equal("bugfix", identity.BuildChannel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bugfix")]
    [InlineData("bugfix0")]
    [InlineData("bugfix-1")]
    [InlineData("BugFix1")]
    [InlineData(" bugfix1")]
    [InlineData("bugfix1 ")]
    [InlineData("hotfix1")]
    public void Detect_InvalidBugFixMarker_FallsBackToStable(string marker)
    {
        using var folder = TemporaryFolder.Create();
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), marker);

        var identity = ApplicationBuildIdentity.Detect("3.1.10", folder.Path);

        Assert.Equal(ApplicationUpdateChannel.Stable, identity.Channel);
        Assert.Equal(0, identity.BugFixSequence);
    }

    [Fact]
    public void Detect_BetaMarker_PreservesExistingBetaIdentity()
    {
        using var folder = TemporaryFolder.Create();
        File.WriteAllText(Path.Combine(folder.Path, "beta-build.flag"), "beta3");
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix2");

        var identity = ApplicationBuildIdentity.Detect("3.1.10", folder.Path);

        Assert.Equal(ApplicationUpdateChannel.Beta, identity.Channel);
        Assert.Equal("beta3", identity.ChannelIdentity);
        Assert.Equal("Beta 3", identity.DisplayLabel);
        Assert.Equal("3.1.10-beta3", identity.UpdateVersion);
        Assert.Equal("beta3", identity.BuildChannel);
    }

    [Fact]
    public void Detect_TestMarker_TakesPrecedenceOverOtherMarkers()
    {
        using var folder = TemporaryFolder.Create();
        File.WriteAllText(Path.Combine(folder.Path, "test-build.flag"), "test-build");
        File.WriteAllText(Path.Combine(folder.Path, "beta-build.flag"), "beta3");
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix2");

        var identity = ApplicationBuildIdentity.Detect("3.1.10", folder.Path);

        Assert.True(identity.IsTestBuild);
        Assert.Equal(ApplicationUpdateChannel.Stable, identity.Channel);
        Assert.Equal("test", identity.BuildChannel);
        Assert.Equal("3.1.10", identity.UpdateVersion);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private TemporaryFolder(string path) => Path = path;
        public string Path { get; }

        public static TemporaryFolder Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"CrystalRelayIdentity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryFolder(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
```

Create `VrcTwitchOscBridge.Tests/BugFixUpdateUiTests.cs` with the complete source-test fixture:

```csharp
using System.Runtime.CompilerServices;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BugFixUpdateUiTests
{
[Fact]
public void MainWindow_HasDedicatedBugFixBadge()
{
    var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml"));
    Assert.Contains("Visibility=\"{Binding HasBugFixBuildLabel", xaml, StringComparison.Ordinal);
    Assert.Contains("Text=\"{Binding BugFixBuildBadgeText}\"", xaml, StringComparison.Ordinal);
}

[Fact]
public void MainWindowViewModel_UsesApplicationBuildIdentity()
{
    var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ViewModels", "MainWindowViewModel.cs"));
    Assert.Contains("ApplicationBuildIdentity.Detect", source, StringComparison.Ordinal);
    Assert.Contains("AppBuildIdentity.HasBugFixLabel", source, StringComparison.Ordinal);
    Assert.Contains("AppBuildIdentity.UpdateVersion", source, StringComparison.Ordinal);
    Assert.DoesNotContain("private static string DetectBetaBuildLabel()", source, StringComparison.Ordinal);
    Assert.DoesNotContain("private static string GetAppUpdateVersion()", source, StringComparison.Ordinal);
}

private static string FindSourceFile(params string[] parts)
{
    var testPath = GetTestPath();
    var testDirectory = Path.GetDirectoryName(testPath)!;
    var repoRoot = Directory.GetParent(testDirectory)!.FullName;
    return Path.Combine([repoRoot, .. parts]);
}

private static string GetTestPath([CallerFilePath] string path = "") => path;
}
```

- [ ] **Step 2: Run marker/UI tests and verify RED**

Run:

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ApplicationBuildIdentityTests|FullyQualifiedName~BugFixUpdateUiTests"
```

Expected: build failure because `ApplicationBuildIdentity` and Bug Fix bindings do not exist.

- [ ] **Step 3: Implement `ApplicationBuildIdentity`**

Create `VrcTwitchOscBridge/Services/ApplicationBuildIdentity.cs`:

```csharp
using System.Globalization;

namespace VrcTwitchOscBridge.Services;

internal sealed record ApplicationBuildIdentity(
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
```

Explicitly include it in the app project:

```xml
<Compile Include="Services\ApplicationBuildIdentity.cs" />
```

- [ ] **Step 4: Replace MainWindowViewModel's loose marker fields**

Remove `TestBuildMarkerFileName` and `BetaBuildMarkerFileName`, then replace the marker-related static fields with:

```csharp
private static readonly string AppVersion = GetAppVersion();
private static readonly ApplicationBuildIdentity AppBuildIdentity =
    ApplicationBuildIdentity.Detect(AppVersion, AppContext.BaseDirectory);
private static readonly string BetaBuildLabel = AppBuildIdentity.HasBetaLabel
    ? AppBuildIdentity.DisplayLabel
    : string.Empty;
private static readonly string AppUpdateVersion = AppBuildIdentity.UpdateVersion;
private static readonly bool IsTestBuild = AppBuildIdentity.IsTestBuild;
private static readonly string BuildChannel = AppBuildIdentity.BuildChannel;
```

Delete `DetectBetaBuildLabel`, `DetectTestBuild`, `GetAppUpdateVersion`, and `GetBuildChannel` after all references use `AppBuildIdentity`.

Replace the live-feedback heartbeat version argument `GetAppUpdateVersion()` with the already-computed `AppUpdateVersion` field:

```csharp
liveFeedbackHeartbeatService.UpdateState(
    Settings.LiveFeedbackHeartbeatEnabled,
    HasRecoverableBroadcasterSession && !broadcasterReconnectRequired,
    IsBroadcasterLive,
    string.IsNullOrWhiteSpace(Settings.Broadcaster.DisplayName)
        ? Settings.Broadcaster.Login
        : Settings.Broadcaster.DisplayName,
    Settings.Broadcaster.Login,
    runtimeConfig.LiveFeedbackHeartbeatEndpoint,
    AppUpdateVersion,
    BuildChannel);
```

Update version display and add badge properties:

```csharp
public string HomeVersionDisplay
{
    get
    {
        var display = $"v{AppVersion}";
        if (!string.IsNullOrWhiteSpace(BetaBuildLabel))
        {
            display += $" {BetaBuildLabel}";
        }
        return display;
    }
}

public bool HasBetaBuildLabel => AppBuildIdentity.HasBetaLabel;
public bool HasBugFixBuildLabel => AppBuildIdentity.HasBugFixLabel;
public string BugFixBuildBadgeText => AppBuildIdentity.HasBugFixLabel
    ? TF("BUG FIX {0}", AppBuildIdentity.BugFixSequence)
    : string.Empty;
```

Replace `GetAppVersionDisplay` with this implementation. Bug Fix builds use the exact update identity in title/build surfaces, while beta display and test/debug suffix behavior remain unchanged:

```csharp
private static string GetAppVersionDisplay()
{
    var builder = new StringBuilder(
        AppBuildIdentity.HasBugFixLabel
            ? AppBuildIdentity.UpdateVersion
            : AppVersion);
    if (AppBuildIdentity.HasBetaLabel)
    {
        builder.Append(" - ");
        builder.Append(AppBuildIdentity.DisplayLabel);
    }

    if (IsTestBuild)
    {
        builder.Append(LocalizationService.Translate(" - Test Build"));
    }

#if DEBUG
    builder.Append(" - DEBUG");
#endif

    return builder.ToString();
}
```

- [ ] **Step 5: Add the Home Bug Fix badge**

Add this border immediately after the existing beta badge in `MainWindow.xaml`:

```xml
<Border Margin="8,0,0,0"
        Padding="6,1,6,2"
        CornerRadius="4"
        Background="{DynamicResource AccentBrush}"
        BorderThickness="0"
        VerticalAlignment="Center"
        Opacity="0.9"
        Visibility="{Binding HasBugFixBuildLabel, Converter={StaticResource BoolToVisibilityConverter}}">
    <TextBlock Text="{Binding BugFixBuildBadgeText}"
               FontSize="10"
               FontWeight="SemiBold"
               Foreground="White" />
</Border>
```

- [ ] **Step 6: Run marker/UI tests and verify GREEN**

Run the Task 3 test command.

Expected: all marker and badge tests pass.

- [ ] **Step 7: Commit checkpoint only when explicitly authorized**

```powershell
git add -- "VrcTwitchOscBridge/Services/ApplicationBuildIdentity.cs" "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs" "VrcTwitchOscBridge/MainWindow.xaml" "VrcTwitchOscBridge.Tests/ApplicationBuildIdentityTests.cs" "VrcTwitchOscBridge.Tests/BugFixUpdateUiTests.cs"
git commit -m "feat(update): detect bug fix build identity"
```

Without explicit authorization, skip these commands.

---

### Task 4: Discover And Prioritize Bug Fix GitHub Releases

**Files:**
- Modify: `VrcTwitchOscBridge/Services/ApplicationUpdateService.cs`
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs:9266-9276`
- Test: `VrcTwitchOscBridge.Tests/ApplicationUpdateServiceTests.cs`

**Interfaces:**
- Consumes: `ApplicationBuildIdentity` and `ApplicationUpdatePackageRules`.
- Produces: `ApplicationUpdateInfo.Channel`, `IsBeta`, `IsBugFix`, `BugFixSequence`, `ReleaseTitle`, and `ReleaseBody`; channel-aware `CheckForUpdateAsync` used by MainWindowViewModel.

- [ ] **Step 1: Write failing end-to-end discovery tests with a fake HTTP handler**

Create `VrcTwitchOscBridge.Tests/ApplicationUpdateServiceTests.cs`. The file must include these tests:

```csharp
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ApplicationUpdateServiceTests
{
    [Fact]
    public async Task BugFix_ExactBase_ReturnsHighestSequenceWithReleaseNotes()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix1", ApplicationUpdateChannel.BugFix, body: "Fix one"),
            Release("v3.1.10-bugfix2", ApplicationUpdateChannel.BugFix, body: "Fix two\n- Full detail"));
        var build = Stable("3.1.10");

        var result = await service.CheckForUpdateAsync(build, "", "", includeBetaUpdates: false);

        var update = Assert.IsType<ApplicationUpdateInfo>(result.Update);
        Assert.Equal(ApplicationUpdateChannel.BugFix, update.Channel);
        Assert.True(update.IsBugFix);
        Assert.False(update.IsBeta);
        Assert.Equal(2, update.BugFixSequence);
        Assert.Equal("3.1.10", update.CurrentVersion);
        Assert.Equal("3.1.10-bugfix2", update.LatestVersion);
        Assert.Equal("3.1.10", update.LatestBaseVersion);
        Assert.Equal("Crystal Relay v3.1.10 Bug Fix Push 2", update.ReleaseTitle);
        Assert.Equal("Fix two\n- Full detail", update.ReleaseBody);
        Assert.Equal(
            "https://github.com/seluvia/crystal-relay-public/releases/tag/v3.1.10-bugfix2",
            update.ReleasePageUrl);
        Assert.Equal("CrystalRelayBugFix-v3.1.10-bugfix2-win-x64.zip", update.AssetName);
        Assert.Equal(
            "https://github.com/seluvia/crystal-relay-public/releases/download/v3.1.10-bugfix2/CrystalRelayBugFix-v3.1.10-bugfix2-win-x64.zip",
            update.AssetDownloadUrl);
        Assert.Equal(1024L, update.AssetSizeBytes);
        Assert.Equal($"sha256:{new string('a', 64)}", update.Sha256Digest);
    }

    [Fact]
    public async Task BugFix_DoesNotApplyToDifferentStableBase()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix1", ApplicationUpdateChannel.BugFix));

        var result = await service.CheckForUpdateAsync(Stable("3.1.9"), "", "", false);

        Assert.Equal(ApplicationUpdateCheckStatus.NoUpdate, result.Status);
    }

    [Fact]
    public async Task OlderClient_ReceivesNormalStableBeforeThatStableBugFix()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix2", ApplicationUpdateChannel.BugFix),
            Release("v3.1.10", ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(Stable("3.1.9"), "", "", false);

        Assert.Equal(ApplicationUpdateChannel.Stable, result.Update!.Channel);
        Assert.Equal("3.1.10", result.Update.LatestVersion);
    }

    [Fact]
    public async Task NewerStable_SupersedesMatchingBugFix()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix2", ApplicationUpdateChannel.BugFix),
            Release("v3.1.11", ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(Stable("3.1.10"), "", "", true);

        Assert.Equal(ApplicationUpdateChannel.Stable, result.Update!.Channel);
        Assert.Equal("3.1.11", result.Update.LatestVersion);
    }

    [Fact]
    public async Task IgnoredNewerStable_DoesNotSuppressMatchingBugFix()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix2", ApplicationUpdateChannel.BugFix),
            Release("v3.1.11", ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(
            Stable("3.1.10"),
            ignoredVersionText: "3.1.11",
            ignoredBetaBaseVersionText: string.Empty,
            includeBetaUpdates: false);

        Assert.Equal(ApplicationUpdateChannel.BugFix, result.Update!.Channel);
        Assert.Equal("3.1.10-bugfix2", result.Update.LatestVersion);
    }

    [Fact]
    public async Task BugFix_PrecedesOptionalBeta_AndIgnoresSkipSettings()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix1", ApplicationUpdateChannel.BugFix),
            Release("v3.1.11-beta1", ApplicationUpdateChannel.Beta));

        var result = await service.CheckForUpdateAsync(
            Stable("3.1.10"),
            ignoredVersionText: "3.1.10-bugfix1",
            ignoredBetaBaseVersionText: "3.1.11",
            includeBetaUpdates: true);

        Assert.Equal(ApplicationUpdateChannel.BugFix, result.Update!.Channel);
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Beta, false)]
    [InlineData(ApplicationUpdateChannel.Stable, true)]
    public async Task BugFix_TargetsOnlyStableOrBugFixBuilds(
        ApplicationUpdateChannel installedChannel,
        bool expectedUpdate)
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix1", ApplicationUpdateChannel.BugFix));
        var build = new ApplicationBuildIdentity(
            "3.1.10",
            installedChannel,
            installedChannel == ApplicationUpdateChannel.Beta ? "beta1" : string.Empty,
            installedChannel == ApplicationUpdateChannel.Beta ? "Beta 1" : string.Empty,
            BugFixSequence: 0,
            IsTestBuild: false);

        var result = await service.CheckForUpdateAsync(build, "", "", true);

        Assert.Equal(expectedUpdate, result.Update is not null);
    }

    [Fact]
    public async Task BugFix_DoesNotTargetTestBuild()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix1", ApplicationUpdateChannel.BugFix));
        var build = new ApplicationBuildIdentity(
            "3.1.10",
            ApplicationUpdateChannel.Stable,
            string.Empty,
            string.Empty,
            BugFixSequence: 0,
            IsTestBuild: true);

        var result = await service.CheckForUpdateAsync(build, "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task InstalledBugFix_DoesNotReofferSameSequenceOrBaseStable()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix1", ApplicationUpdateChannel.BugFix),
            Release("v3.1.10", ApplicationUpdateChannel.Stable));
        var build = new ApplicationBuildIdentity(
            "3.1.10",
            ApplicationUpdateChannel.BugFix,
            "bugfix1",
            "Bug Fix 1",
            BugFixSequence: 1,
            IsTestBuild: false);

        var result = await service.CheckForUpdateAsync(build, "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task InstalledBugFix_ReceivesOnlyGreaterCumulativeSequence()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix1", ApplicationUpdateChannel.BugFix),
            Release("v3.1.10-bugfix2", ApplicationUpdateChannel.BugFix),
            Release("v3.1.10-bugfix3", ApplicationUpdateChannel.BugFix));
        var build = new ApplicationBuildIdentity(
            "3.1.10",
            ApplicationUpdateChannel.BugFix,
            "bugfix2",
            "Bug Fix 2",
            BugFixSequence: 2,
            IsTestBuild: false);

        var result = await service.CheckForUpdateAsync(build, "", "", false);

        Assert.Equal("3.1.10-bugfix2", result.Update!.CurrentVersion);
        Assert.Equal("3.1.10-bugfix3", result.Update!.LatestVersion);
    }

    [Theory]
    [InlineData("v3.1.10-bugfix")]
    [InlineData("v3.1.10-bugfix0")]
    [InlineData("v3.1.10-bugfix-1")]
    [InlineData("3.1.10-bugfix1")]
    [InlineData("V3.1.10-bugfix1")]
    [InlineData("v3.1.10-BugFix1")]
    [InlineData("v3.1.10-bugfix1-extra")]
    [InlineData(" v3.1.10-bugfix1")]
    [InlineData("v3.1.10-bugfix1 ")]
    [InlineData("v3.1.10-hotfix1")]
    public async Task MalformedBugFixTags_AreIgnored(string tag)
    {
        using var service = CreateService(Release(tag, ApplicationUpdateChannel.BugFix));

        var result = await service.CheckForUpdateAsync(Stable("3.1.10"), "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task BugFix_WithStableAssetSubstitution_IsIgnored()
    {
        using var service = CreateService(Release(
            "v3.1.10-bugfix1",
            ApplicationUpdateChannel.BugFix,
            assetChannel: ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(Stable("3.1.10"), "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task BugFix_WithWrongAssetCasing_IsIgnored()
    {
        using var service = CreateService(Release(
            "v3.1.10-bugfix1",
            ApplicationUpdateChannel.BugFix,
            assetNameOverride: "crystalrelaybugfix-v3.1.10-bugfix1-win-x64.zip"));

        var result = await service.CheckForUpdateAsync(Stable("3.1.10"), "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task BugFix_MarkedAsPrerelease_IsIgnored()
    {
        using var service = CreateService(Release(
            "v3.1.10-bugfix1",
            ApplicationUpdateChannel.BugFix,
            prerelease: true));

        var result = await service.CheckForUpdateAsync(Stable("3.1.10"), "", "", false);

        Assert.Null(result.Update);
    }

    [Fact]
    public async Task MalformedBugFix_DoesNotBlockValidStableCandidate()
    {
        using var service = CreateService(
            Release("v3.1.10-bugfix0", ApplicationUpdateChannel.BugFix),
            Release("v3.1.11", ApplicationUpdateChannel.Stable));

        var result = await service.CheckForUpdateAsync(Stable("3.1.10"), "", "", false);

        Assert.Equal(ApplicationUpdateChannel.Stable, result.Update!.Channel);
    }

    [Fact]
    public async Task StableSelection_RemainsAvailableWithoutBetaOptIn()
    {
        using var service = CreateService(
            Release("v3.1.10", ApplicationUpdateChannel.Stable),
            Release("v3.1.11-beta1", ApplicationUpdateChannel.Beta));

        var result = await service.CheckForUpdateAsync(Stable("3.1.9"), "", "", false);

        Assert.Equal(ApplicationUpdateChannel.Stable, result.Update!.Channel);
        Assert.Equal("3.1.10", result.Update.LatestVersion);
    }

    [Fact]
    public async Task BetaSelection_RemainsAvailableWhenEnabledAndNoStableExists()
    {
        using var service = CreateService(
            Release("v3.1.11-beta1", ApplicationUpdateChannel.Beta));

        var result = await service.CheckForUpdateAsync(Stable("3.1.10"), "", "", true);

        Assert.Equal(ApplicationUpdateChannel.Beta, result.Update!.Channel);
        Assert.Equal("3.1.11-beta1", result.Update.LatestVersion);
    }

    [Fact]
    public async Task Discovery_UsesOneRequestWithOneHundredReleasePageSize()
    {
        var handler = new ReleaseHandler([]);
        using var client = new HttpClient(handler);
        using var service = new ApplicationUpdateService(client);

        await service.CheckForUpdateAsync(Stable("3.1.10"), "", "", false);

        Assert.Single(handler.RequestUris);
        Assert.Contains("per_page=100", handler.RequestUris[0].Query, StringComparison.Ordinal);
    }

    private static ApplicationBuildIdentity Stable(string version) =>
        new(version, ApplicationUpdateChannel.Stable, string.Empty, string.Empty, 0, false);

    private static ApplicationUpdateService CreateService(params object[] releases)
    {
        var handler = new ReleaseHandler(releases);
        return new ApplicationUpdateService(new HttpClient(handler));
    }

    private static object Release(
        string tag,
        ApplicationUpdateChannel channel,
        string body = "Release details",
        ApplicationUpdateChannel? assetChannel = null,
        bool? prerelease = null,
        string? assetNameOverride = null)
    {
        var assetName = assetNameOverride
            ?? ApplicationUpdatePackageRules.GetExpectedAssetName(
                assetChannel ?? channel,
                tag.TrimStart('v', 'V'));
        return new
        {
            tag_name = tag,
            name = channel == ApplicationUpdateChannel.BugFix
                ? $"Crystal Relay v3.1.10 Bug Fix Push {ExtractSequence(tag)}"
                : tag,
            body,
            html_url = $"https://github.com/seluvia/crystal-relay-public/releases/tag/{tag}",
            draft = false,
            prerelease = prerelease ?? channel == ApplicationUpdateChannel.Beta,
            published_at = "2026-07-19T00:00:00Z",
            assets = new[]
            {
                new
                {
                    name = assetName,
                    browser_download_url = $"https://github.com/seluvia/crystal-relay-public/releases/download/{tag}/{assetName}",
                    size = 1024,
                    digest = $"sha256:{new string('a', 64)}"
                }
            }
        };
    }

    private static int ExtractSequence(string tag)
    {
        var marker = "bugfix";
        var index = tag.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 && int.TryParse(tag[(index + marker.Length)..], out var sequence)
            ? sequence
            : 0;
    }

    private sealed class ReleaseHandler(IEnumerable<object> releases) : HttpMessageHandler
    {
        private readonly string json = JsonSerializer.Serialize(releases);
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
```

- [ ] **Step 2: Run discovery tests and verify RED**

Run:

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ApplicationUpdateServiceTests"
```

Expected: compile failures because the service lacks the injected constructor, channel-aware API, and metadata properties.

- [ ] **Step 3: Replace the update info model and add deterministic HTTP injection**

Use this exact model:

```csharp
internal sealed record ApplicationUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string LatestBaseVersion,
    ApplicationUpdateChannel Channel,
    int BugFixSequence,
    string ReleaseTitle,
    string ReleaseBody,
    string ReleasePageUrl,
    string AssetName,
    string AssetDownloadUrl,
    long AssetSizeBytes,
    string Sha256Digest)
{
    public bool IsBeta => Channel == ApplicationUpdateChannel.Beta;
    public bool IsBugFix => Channel == ApplicationUpdateChannel.BugFix;
}
```

Change the endpoint to:

```csharp
private static readonly Uri ReleasesEndpoint = new(
    "https://api.github.com/repos/seluvia/crystal-relay-public/releases?per_page=100");
```

Replace the field initializer/constructor with:

```csharp
private readonly HttpClient httpClient;

public ApplicationUpdateService()
    : this(new HttpClient { Timeout = DefaultRequestTimeout })
{
}

internal ApplicationUpdateService(HttpClient httpClient)
{
    this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    this.httpClient.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelay-DesktopApp");
}
```

- [ ] **Step 4: Implement exact Bug Fix classification and candidate shape**

Replace `ReleaseCandidate` with the channel-aware shape:

```csharp
private sealed record ReleaseCandidate(
    AppReleaseVersion Version,
    ApplicationUpdateChannel Channel,
    int BugFixSequence,
    string ReleaseTitle,
    string ReleaseBody,
    string ReleasePageUrl,
    string AssetName,
    string AssetDownloadUrl,
    long AssetSizeBytes,
    string Sha256Digest,
    DateTimeOffset PublishedAt);
```

Add `Body` to `GitHubReleaseResponse`:

```csharp
[JsonPropertyName("body")]
public string? Body { get; set; }
```

Implement Bug Fix parsing before beta/stable classification:

```csharp
private static bool TryCreateCandidate(GitHubReleaseResponse release, out ReleaseCandidate candidate)
{
    candidate = default!;
    ApplicationUpdateChannel channel;
    AppReleaseVersion version;
    var bugFixSequence = 0;

    var tagText = release.TagName ?? string.Empty;
    if (tagText.StartsWith("v", StringComparison.Ordinal)
        && ApplicationUpdatePackageRules.TryParseBugFixVersion(
            tagText[1..],
            out var bugFixBaseVersion,
            out bugFixSequence))
    {
        if (release.Prerelease
            || !TryParseReleaseVersion(bugFixBaseVersion, out var baseVersion))
        {
            return false;
        }

        channel = ApplicationUpdateChannel.BugFix;
        version = baseVersion with { Prerelease = $"bugfix{bugFixSequence}" };
    }
    else
    {
        if (!TryParseReleaseVersion(release.TagName, out version))
        {
            return false;
        }

        var isBeta = IsBetaRelease(release, version);
        if (!isBeta && (release.Prerelease || version.IsPrerelease))
        {
            return false;
        }

        channel = isBeta ? ApplicationUpdateChannel.Beta : ApplicationUpdateChannel.Stable;
    }

    if (!TryFindReleaseAsset(release, version, channel, out var asset))
    {
        return false;
    }

    candidate = new ReleaseCandidate(
        version,
        channel,
        bugFixSequence,
        string.IsNullOrWhiteSpace(release.Name) ? version.ToDisplayString() : release.Name.Trim(),
        release.Body ?? string.Empty,
        release.HtmlUrl ?? string.Empty,
        asset.Name ?? string.Empty,
        asset.BrowserDownloadUrl ?? string.Empty,
        Math.Max(0, asset.Size),
        asset.Digest ?? string.Empty,
        release.PublishedAt ?? DateTimeOffset.MinValue);
    return true;
}

private static bool TryFindReleaseAsset(
    GitHubReleaseResponse release,
    AppReleaseVersion version,
    ApplicationUpdateChannel channel,
    out GitHubReleaseAssetResponse asset)
{
    asset = default!;
    var expectedAssetName = ApplicationUpdatePackageRules.GetExpectedAssetName(
        channel,
        version.ToDisplayString());
    var nameComparison = channel == ApplicationUpdateChannel.BugFix
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;
    var match = (release.Assets ?? [])
        .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name))
        .FirstOrDefault(candidate => string.Equals(
            candidate.Name,
            expectedAssetName,
            nameComparison));
    if (match is null || string.IsNullOrWhiteSpace(match.BrowserDownloadUrl))
    {
        return false;
    }

    asset = match;
    return true;
}
```

Delete the obsolete `RuntimeName` constant, local `GetExpectedAssetName` method, and `ChooseBestCandidate` method. `ApplicationUpdatePackageRules` now owns asset naming, and Step 5 performs explicit three-channel precedence.

- [ ] **Step 5: Implement stable > Bug Fix > beta selection**

Replace `CheckForUpdateAsync` with the complete channel-aware method:

```csharp
public async Task<ApplicationUpdateCheckResult> CheckForUpdateAsync(
    ApplicationBuildIdentity currentBuild,
    string ignoredVersionText,
    string ignoredBetaBaseVersionText,
    bool includeBetaUpdates,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(currentBuild);
    if (!TryParseReleaseVersion(currentBuild.UpdateVersion, out var currentVersion))
    {
        return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
    }

    List<GitHubReleaseResponse>? releases;
    try
    {
        using var response = await httpClient.GetAsync(ReleasesEndpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.RequestFailed);
        }

        releases = await response.Content.ReadFromJsonAsync<List<GitHubReleaseResponse>>(
            cancellationToken: cancellationToken);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch
    {
        return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.RequestFailed);
    }

    if (releases is null || releases.Count == 0)
    {
        return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
    }

    var candidates = releases
        .Where(release => !release.Draft && !string.IsNullOrWhiteSpace(release.HtmlUrl))
        .Select(release => TryCreateCandidate(release, out var candidate) ? candidate : null)
        .OfType<ReleaseCandidate>()
        .ToArray();
    if (candidates.Length == 0)
    {
        return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.ReleaseVersionUnreadable);
    }

    var betaUpdatesAllowed = includeBetaUpdates
        || currentBuild.Channel == ApplicationUpdateChannel.Beta;
    var bestStable = candidates
        .Where(candidate => candidate.Channel == ApplicationUpdateChannel.Stable)
        .Where(candidate => IsNewerStableCandidate(candidate.Version, currentVersion, currentBuild.Channel))
        .Where(candidate => !IsIgnoredUpdate(ignoredVersionText, candidate.Version, betaCandidate: false))
        .OrderByDescending(candidate => candidate.Version)
        .ThenByDescending(candidate => candidate.PublishedAt)
        .FirstOrDefault();

    var bestBugFix = !currentBuild.IsTestBuild
        && currentBuild.Channel is ApplicationUpdateChannel.Stable or ApplicationUpdateChannel.BugFix
            ? candidates
                .Where(candidate => candidate.Channel == ApplicationUpdateChannel.BugFix)
                .Where(candidate => candidate.Version.CompareBaseTo(currentVersion) == 0)
                .Where(candidate => candidate.BugFixSequence > currentBuild.BugFixSequence)
                .OrderByDescending(candidate => candidate.BugFixSequence)
                .ThenByDescending(candidate => candidate.PublishedAt)
                .FirstOrDefault()
            : null;

    var bestBeta = betaUpdatesAllowed
        ? candidates
            .Where(candidate => candidate.Channel == ApplicationUpdateChannel.Beta)
            .Where(candidate => IsNewerBetaCandidate(candidate.Version, currentVersion))
            .Where(candidate => !IsIgnoredUpdate(ignoredVersionText, candidate.Version, betaCandidate: true))
            .Where(candidate => !IsIgnoredBetaBaseVersion(ignoredBetaBaseVersionText, candidate.Version))
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.PublishedAt)
            .FirstOrDefault()
        : null;

    // Bug Fix candidates intentionally bypass both persistent ignore helpers.
    var update = bestStable ?? bestBugFix ?? bestBeta;
    if (update is null)
    {
        return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
    }

    return new ApplicationUpdateCheckResult(
        ApplicationUpdateCheckStatus.UpdateAvailable,
        new ApplicationUpdateInfo(
            currentBuild.UpdateVersion,
            update.Version.ToDisplayString(),
            update.Version.ToBaseDisplayString(),
            update.Channel,
            update.BugFixSequence,
            update.ReleaseTitle,
            update.ReleaseBody,
            update.ReleasePageUrl,
            update.AssetName,
            update.AssetDownloadUrl,
            update.AssetSizeBytes,
            update.Sha256Digest));
}
```

Add the stable comparison helper used above:

```csharp
private static bool IsNewerStableCandidate(
    AppReleaseVersion stableVersion,
    AppReleaseVersion currentVersion,
    ApplicationUpdateChannel currentChannel) =>
    currentChannel == ApplicationUpdateChannel.BugFix
        ? stableVersion.CompareBaseTo(currentVersion) > 0
        : stableVersion.CompareTo(currentVersion) > 0;
```

- [ ] **Step 6: Pass the installed identity from MainWindowViewModel**

Change the service call to:

```csharp
result = await applicationUpdateService.CheckForUpdateAsync(
    AppBuildIdentity,
    Settings.IgnoredUpdateVersion,
    Settings.IgnoredBetaUpdateBaseVersion,
    Settings.BetaApplicationUpdatesEnabled || AppBuildIdentity.Channel == ApplicationUpdateChannel.Beta,
    cancellationToken);
```

- [ ] **Step 7: Run discovery tests and verify GREEN**

Run the Task 4 test command.

Expected: all update-discovery tests pass and existing stable/beta behavior remains green.

- [ ] **Step 8: Commit checkpoint only when explicitly authorized**

```powershell
git add -- "VrcTwitchOscBridge/Services/ApplicationUpdateService.cs" "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs" "VrcTwitchOscBridge.Tests/ApplicationUpdateServiceTests.cs"
git commit -m "feat(update): discover mandatory bug fix pushes"
```

Without explicit authorization, skip these commands.

---

### Task 5: Validate And Apply Bug Fix Packages In Place

**Files:**
- Modify: `VrcTwitchOscBridge/Services/ApplicationSelfUpdateService.cs`
- Modify: `CrystalRelayUpdater/Program.cs`
- Test: `VrcTwitchOscBridge.Tests/ApplicationSelfUpdateBugFixTests.cs`

**Interfaces:**
- Consumes: `ApplicationUpdateInfo.Channel` and shared package rules.
- Produces: exact Bug Fix asset, manifest, entry-executable, and marker validation; static/versioned executable fallback compatibility; and an apply plan whose target remains `InstallDirectory` in both executables.

- [ ] **Step 1: Write failing self-update and updater-parity tests**

Create `VrcTwitchOscBridge.Tests/ApplicationSelfUpdateBugFixTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ApplicationSelfUpdateBugFixTests
{
    [Fact]
    public void ValidateReleaseAsset_AcceptsDedicatedBugFixAsset()
    {
        var update = CreateBugFixUpdate("CrystalRelayBugFix-v3.1.10-bugfix1-win-x64.zip");
        ApplicationSelfUpdateService.ValidateReleaseAsset(update);
    }

    [Fact]
    public void ValidateReleaseAsset_RejectsStableAssetForBugFixChannel()
    {
        var update = CreateBugFixUpdate("CrystalRelayTwitchOsc-v3.1.10-bugfix1-win-x64.zip");
        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidateReleaseAsset(update));
    }

    [Fact]
    public void ValidateReleaseAsset_RejectsWrongBugFixAssetCasing()
    {
        var update = CreateBugFixUpdate("crystalrelaybugfix-v3.1.10-bugfix1-win-x64.zip");
        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidateReleaseAsset(update));
    }

    [Fact]
    public void ValidatePackageManifest_AcceptsBugFixChannelAndVersion()
    {
        var update = CreateBugFixUpdate("CrystalRelayBugFix-v3.1.10-bugfix1-win-x64.zip");
        var manifest = new ApplicationUpdatePackageManifest(
            "Crystal Relay",
            "3.1.10-bugfix1",
            "bugfix",
            "win-x64",
            "CrystalRelayTwitchOsc-v3.1.10-bugfix1.exe");

        ApplicationSelfUpdateService.ValidatePackageManifest(manifest, update);
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("beta")]
    [InlineData("test")]
    public void ValidatePackageManifest_RejectsWrongBugFixChannel(string channel)
    {
        var update = CreateBugFixUpdate("CrystalRelayBugFix-v3.1.10-bugfix1-win-x64.zip");
        var manifest = new ApplicationUpdatePackageManifest(
            "Crystal Relay",
            "3.1.10-bugfix1",
            channel,
            "win-x64",
            "CrystalRelayTwitchOsc-v3.1.10-bugfix1.exe");

        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageManifest(manifest, update));
    }

    [Theory]
    [InlineData("3.1.10-bugfix2", "CrystalRelayTwitchOsc-v3.1.10-bugfix1.exe")]
    [InlineData("3.1.10-bugfix1", "Crystal Relay.exe")]
    public void ValidatePackageManifest_RejectsWrongVersionOrEntry(
        string version,
        string entryExecutableName)
    {
        var update = CreateBugFixUpdate("CrystalRelayBugFix-v3.1.10-bugfix1-win-x64.zip");
        var manifest = new ApplicationUpdatePackageManifest(
            "Crystal Relay",
            version,
            "bugfix",
            "win-x64",
            entryExecutableName);

        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageManifest(manifest, update));
    }

    [Fact]
    public void ValidatePackageMarker_RequiresExactBugFixMarker()
    {
        using var folder = TemporaryFolder.Create();
        var manifest = CreateBugFixManifest();
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix1");

        ApplicationSelfUpdateService.ValidatePackageMarker(folder.Path, manifest);

        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix2");
        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageMarker(folder.Path, manifest));

        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix1\n");
        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageMarker(folder.Path, manifest));
    }

    [Fact]
    public void ValidatePackageMarker_RejectsMissingBugFixMarker()
    {
        using var folder = TemporaryFolder.Create();

        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageMarker(folder.Path, CreateBugFixManifest()));
    }

    [Fact]
    public void MainAndDedicatedUpdater_UseSharedInstallTargetPolicy()
    {
        var mainSource = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));
        var updaterProject = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "CrystalRelayUpdater.csproj"));

        Assert.Contains("ApplicationUpdatePackageRules.GetInstallTargetDirectory", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.GetInstallTargetDirectory", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.TryParseManifestChannel", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.TryParseManifestChannel", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedEntryExecutableName", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedEntryExecutableName", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedBuildMarker", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedBuildMarker", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsApplicationExecutableName", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsApplicationExecutableName", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.cs", updaterProject, StringComparison.Ordinal);
    }

    private static ApplicationUpdatePackageManifest CreateBugFixManifest() => new(
        "Crystal Relay",
        "3.1.10-bugfix1",
        "bugfix",
        "win-x64",
        "CrystalRelayTwitchOsc-v3.1.10-bugfix1.exe");

    private static ApplicationUpdateInfo CreateBugFixUpdate(string assetName) => new(
        CurrentVersion: "3.1.10",
        LatestVersion: "3.1.10-bugfix1",
        LatestBaseVersion: "3.1.10",
        Channel: ApplicationUpdateChannel.BugFix,
        BugFixSequence: 1,
        ReleaseTitle: "Crystal Relay v3.1.10 Bug Fix Push 1",
        ReleaseBody: "Fix details",
        ReleasePageUrl: "https://github.com/seluvia/crystal-relay-public/releases/tag/v3.1.10-bugfix1",
        AssetName: assetName,
        AssetDownloadUrl: $"https://github.com/seluvia/crystal-relay-public/releases/download/v3.1.10-bugfix1/{assetName}",
        AssetSizeBytes: 1024,
        Sha256Digest: $"sha256:{new string('a', 64)}");

    private static string FindSourceFile(params string[] parts)
    {
        var testPath = GetTestPath();
        var testDirectory = Path.GetDirectoryName(testPath)!;
        var repoRoot = Directory.GetParent(testDirectory)!.FullName;
        return Path.Combine([repoRoot, .. parts]);
    }

    private static string GetTestPath([CallerFilePath] string path = "") => path;

    private sealed class TemporaryFolder : IDisposable
    {
        private TemporaryFolder(string path) => Path = path;
        public string Path { get; }

        public static TemporaryFolder Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CrystalRelayUpdate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryFolder(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: Run self-update tests and verify RED**

Run:

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ApplicationSelfUpdateBugFixTests"
```

Expected: compile failures because validation methods are private and do not use channel policy.

- [ ] **Step 3: Make main-app validation channel-aware**

Change `ValidateReleaseAsset` to `internal static` and derive the exact asset name from the channel:

```csharp
internal static void ValidateReleaseAsset(ApplicationUpdateInfo update)
{
    if (string.IsNullOrWhiteSpace(update.AssetName)
        || !update.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
    {
        throw new ApplicationSelfUpdateException("The GitHub release does not include a usable Crystal Relay ZIP asset.");
    }

    var expectedAssetName = ApplicationUpdatePackageRules.GetExpectedAssetName(
        update.Channel,
        update.LatestVersion);
    var nameComparison = update.Channel == ApplicationUpdateChannel.BugFix
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;
    if (!string.Equals(update.AssetName, expectedAssetName, nameComparison))
    {
        throw new ApplicationSelfUpdateException("The GitHub release asset name does not match Crystal Relay's update package format.");
    }

    if (!Uri.TryCreate(update.AssetDownloadUrl, UriKind.Absolute, out var uri)
        || uri.Scheme != Uri.UriSchemeHttps
        || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
    {
        throw new ApplicationSelfUpdateException("The update download URL is not a trusted GitHub HTTPS asset URL.");
    }
}
```

Change `ValidatePackageManifest` to `internal static` and replace its body with:

```csharp
internal static void ValidatePackageManifest(
    ApplicationUpdatePackageManifest manifest,
    ApplicationUpdateInfo update)
{
    if (!string.Equals(manifest.ProductName, ProductName, StringComparison.Ordinal))
    {
        throw new ApplicationSelfUpdateException("The update package is not a Crystal Relay package.");
    }

    if (!string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase))
    {
        throw new ApplicationSelfUpdateException("The update package runtime does not match this Crystal Relay build.");
    }

    if (!ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var manifestChannel)
        || manifestChannel != update.Channel)
    {
        throw new ApplicationSelfUpdateException("The update package channel does not match the selected release.");
    }

    if (!string.Equals(manifest.Version, update.LatestVersion, StringComparison.Ordinal))
    {
        throw new ApplicationSelfUpdateException("The update package version does not match the selected release.");
    }

    if (string.IsNullOrWhiteSpace(manifest.EntryExecutableName)
        || Path.IsPathRooted(manifest.EntryExecutableName)
        || manifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
        || manifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
        || !ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            manifestChannel,
            manifest.Version,
            manifest.EntryExecutableName))
    {
        throw new ApplicationSelfUpdateException("The update package entry executable name is invalid.");
    }
}
```

Replace `ResolvePackage` with this complete method so Bug Fix packages validate their marker and cannot use the legacy manifest-free fallback:

```csharp
private static UpdatePackage ResolvePackage(string stagingRoot, ApplicationUpdateInfo update)
{
    var manifestPaths = Directory.GetFiles(
        stagingRoot,
        PackageManifestFileName,
        SearchOption.AllDirectories);
    if (manifestPaths.Length > 1)
    {
        throw new ApplicationSelfUpdateException("The update package contains more than one Crystal Relay manifest.");
    }

    if (manifestPaths.Length == 1)
    {
        var manifestPath = manifestPaths[0];
        var packageRoot = Path.GetDirectoryName(manifestPath)
            ?? throw new ApplicationSelfUpdateException("The update package manifest path is invalid.");
        var manifest = JsonSerializer.Deserialize<ApplicationUpdatePackageManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions)
            ?? throw new ApplicationSelfUpdateException("The update package manifest could not be read.");
        ValidatePackageManifest(manifest, update);
        ValidatePackageMarker(packageRoot, manifest);

        var entryPath = Path.Combine(packageRoot, manifest.EntryExecutableName);
        if (!File.Exists(entryPath))
        {
            throw new ApplicationSelfUpdateException("The update package entry executable is missing.");
        }

        return new UpdatePackage(packageRoot, manifest, entryPath);
    }

    if (update.IsBugFix)
    {
        throw new ApplicationSelfUpdateException("The Bug Fix update package manifest is missing.");
    }

    var executablePath = ResolveSingleExecutable(stagingRoot);
    var fallbackManifest = new ApplicationUpdatePackageManifest(
        ProductName,
        update.LatestVersion,
        ApplicationUpdatePackageRules.GetManifestChannel(update.Channel),
        RuntimeName,
        Path.GetFileName(executablePath));
    return new UpdatePackage(
        Path.GetDirectoryName(executablePath)!,
        fallbackManifest,
        executablePath);
}
```

Add this directly testable marker validator:

```csharp
internal static void ValidatePackageMarker(
    string packageRoot,
    ApplicationUpdatePackageManifest manifest)
{
    if (!ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var channel))
    {
        throw new ApplicationSelfUpdateException("The update package channel is invalid.");
    }

    var expectedMarker = ApplicationUpdatePackageRules.GetExpectedBuildMarker(
        channel,
        manifest.Version);
    if (channel != ApplicationUpdateChannel.BugFix || string.IsNullOrWhiteSpace(expectedMarker))
    {
        if (channel == ApplicationUpdateChannel.BugFix)
        {
            throw new ApplicationSelfUpdateException("The Bug Fix update package version is invalid.");
        }

        return;
    }

    var markerPath = Path.Combine(packageRoot, "bugfix-build.flag");
    var markerText = File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
    if (!ApplicationUpdatePackageRules.IsExpectedBuildMarker(channel, manifest.Version, markerText))
    {
        throw new ApplicationSelfUpdateException("The Bug Fix update package marker does not match the selected release.");
    }
}
```

In `ValidateApplyManifest`, reject unknown channels and mismatched entries inside the initial completeness guard, then validate the staged marker after `packageRoot` passes its updater-storage check:

```csharp
if (!string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase)
    || !ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var manifestChannel)
    || !ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
        manifestChannel,
        manifest.Version,
        manifest.EntryExecutableName))
{
    throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest channel or entry executable is invalid.");
}

// After IsPathInside(updateRoot, packageRoot) succeeds:
ValidatePackageMarker(
    packageRoot,
    new ApplicationUpdatePackageManifest(
        manifest.ProductName,
        manifest.Version,
        manifest.Channel,
        manifest.Runtime,
        manifest.EntryExecutableName));
```

In `CreateInstallPlan`, parse the already-validated channel and replace local folder-selection logic with:

```csharp
if (!ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var channel))
{
    throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest channel is invalid.");
}

var targetDirectory = NormalizeDirectoryPath(
    ApplicationUpdatePackageRules.GetInstallTargetDirectory(
        channel,
        sourceDirectory,
        packageRoot));
```

Replace `ResolveSingleExecutable` so rollback/fallback supports both the current static executable and the approved versioned Bug Fix executable:

```csharp
private static string ResolveSingleExecutable(string root)
{
    var executables = Directory.GetFiles(root, "*.exe", SearchOption.AllDirectories)
        .Where(path => ApplicationUpdatePackageRules.IsApplicationExecutableName(Path.GetFileName(path)))
        .ToArray();
    return executables.Length == 1
        ? executables[0]
        : throw new ApplicationSelfUpdateException("The update package must contain exactly one Crystal Relay executable.");
}
```

In `TryValidatePackageInstallDirectory`, include parsed channel, expected entry name, and marker validation in the existing manifest checks. Use this block after deserialization and before checking that the entry file exists:

```csharp
if (manifest is null
    || !string.Equals(manifest.ProductName, ProductName, StringComparison.Ordinal)
    || !string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase)
    || !ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var channel)
    || string.IsNullOrWhiteSpace(manifest.EntryExecutableName)
    || Path.IsPathRooted(manifest.EntryExecutableName)
    || manifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
    || manifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
    || !ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
        channel,
        manifest.Version,
        manifest.EntryExecutableName))
{
    validationError = "The package manifest is not a Crystal Relay install manifest.";
    return false;
}

var expectedMarker = ApplicationUpdatePackageRules.GetExpectedBuildMarker(channel, manifest.Version);
if (channel == ApplicationUpdateChannel.BugFix)
{
    var markerPath = Path.Combine(fullDirectoryPath, "bugfix-build.flag");
    var markerText = File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
    if (string.IsNullOrWhiteSpace(expectedMarker)
        || !ApplicationUpdatePackageRules.IsExpectedBuildMarker(channel, manifest.Version, markerText))
    {
        validationError = "The Bug Fix package marker is missing or invalid.";
        return false;
    }
}
```

Keep package-folder validation as a thin shared delegate and remove the now-unused `PackageFolderPrefix` and `ExecutableSearchPattern` constants:

```csharp
private static bool IsPackageInstallFolderName(string? folderName) =>
    ApplicationUpdatePackageRules.IsPackageInstallFolderName(folderName);
```

- [ ] **Step 4: Apply the identical shared policy in the dedicated updater**

Add:

```csharp
using VrcTwitchOscBridge.Services;
```

In `ValidateApplyManifest`, add the same channel/entry check to the initial validation and validate the marker after the package-root containment check:

```csharp
if (!string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase)
    || !ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var manifestChannel)
    || !ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
        manifestChannel,
        manifest.Version,
        manifest.EntryExecutableName))
{
    throw new UpdaterException("The Crystal Relay update apply manifest channel or entry executable is invalid.");
}

// After IsPathInside(updateRoot, packageRoot) succeeds:
ValidateBuildMarker(packageRoot, manifestChannel, manifest.Version);
```

Add the dedicated-updater marker validator:

```csharp
private static void ValidateBuildMarker(
    string packageRoot,
    ApplicationUpdateChannel channel,
    string version)
{
    var expectedMarker = ApplicationUpdatePackageRules.GetExpectedBuildMarker(channel, version);
    if (channel != ApplicationUpdateChannel.BugFix)
    {
        return;
    }

    var markerPath = Path.Combine(packageRoot, "bugfix-build.flag");
    var markerText = File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
    if (string.IsNullOrWhiteSpace(expectedMarker)
        || !ApplicationUpdatePackageRules.IsExpectedBuildMarker(channel, version, markerText))
    {
        throw new UpdaterException("The Bug Fix update package marker is missing or invalid.");
    }
}
```

In `CreateInstallPlan`, replace the folder-name relocation block with the shared channel policy:

```csharp
if (!ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var channel))
{
    throw new UpdaterException("The Crystal Relay update apply manifest channel is invalid.");
}

var targetDirectory = NormalizeDirectoryPath(
    ApplicationUpdatePackageRules.GetInstallTargetDirectory(
        channel,
        sourceDirectory,
        packageRoot));
```

Replace `ResolveSingleExecutable` with the same executable-name filter, changing only the exception type:

```csharp
private static string ResolveSingleExecutable(string root)
{
    var executables = Directory.GetFiles(root, "*.exe", SearchOption.AllDirectories)
        .Where(path => ApplicationUpdatePackageRules.IsApplicationExecutableName(Path.GetFileName(path)))
        .ToArray();
    return executables.Length == 1
        ? executables[0]
        : throw new UpdaterException("The update package must contain exactly one Crystal Relay executable.");
}
```

In the updater's `TryValidatePackageInstallDirectory`, replace the manifest-validity block and add the marker check before the entry-file existence check:

```csharp
if (manifest is null
    || !string.Equals(manifest.ProductName, ProductName, StringComparison.Ordinal)
    || !string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase)
    || !ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var channel)
    || string.IsNullOrWhiteSpace(manifest.EntryExecutableName)
    || Path.IsPathRooted(manifest.EntryExecutableName)
    || manifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
    || manifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
    || !ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
        channel,
        manifest.Version,
        manifest.EntryExecutableName))
{
    validationError = "The package manifest is not a Crystal Relay install manifest.";
    return false;
}

if (channel == ApplicationUpdateChannel.BugFix)
{
    var markerPath = Path.Combine(fullDirectoryPath, "bugfix-build.flag");
    var markerText = File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
    if (string.IsNullOrWhiteSpace(
            ApplicationUpdatePackageRules.GetExpectedBuildMarker(channel, manifest.Version))
        || !ApplicationUpdatePackageRules.IsExpectedBuildMarker(channel, manifest.Version, markerText))
    {
        validationError = "The Bug Fix package marker is missing or invalid.";
        return false;
    }
}
```

Delegate folder-name validation exactly as follows, and remove `PackageFolderPrefix` and `ExecutableSearchPattern`:

```csharp
private static bool IsPackageInstallFolderName(string? folderName) =>
    ApplicationUpdatePackageRules.IsPackageInstallFolderName(folderName);
```

Do not duplicate the asset naming switch in `Program.cs`; the main app validates the package before the dedicated updater starts.

- [ ] **Step 5: Run self-update tests and build both executables**

Run:

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ApplicationSelfUpdateBugFixTests|FullyQualifiedName~ApplicationUpdatePackageRulesTests"
dotnet build "VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
dotnet build "CrystalRelayUpdater\CrystalRelayUpdater.csproj" --no-restore
```

Expected: all targeted tests pass and both builds succeed.

- [ ] **Step 6: Commit checkpoint only when explicitly authorized**

```powershell
git add -- "VrcTwitchOscBridge/Services/ApplicationSelfUpdateService.cs" "CrystalRelayUpdater/Program.cs" "VrcTwitchOscBridge.Tests/ApplicationSelfUpdateBugFixTests.cs"
git commit -m "feat(update): apply bug fix packages in place"
```

Without explicit authorization, skip these commands.

---

### Task 6: Add The Mandatory Scrollable Bug Fix Dialog

**Files:**
- Modify: `VrcTwitchOscBridge/ThemedDialogWindow.xaml`
- Modify: `VrcTwitchOscBridge/ThemedDialogWindow.xaml.cs`
- Modify: `VrcTwitchOscBridge/MainWindow.xaml.cs:1127-1211`
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`
- Modify: every non-English `VrcTwitchOscBridge/Resources/Localization/*.extra.json`
- Test: `VrcTwitchOscBridge.Tests/BugFixUpdateUiTests.cs`

**Interfaces:**
- Consumes: `ApplicationUpdateInfo.IsBugFix`, title/body/base/sequence, existing `StartApplicationSelfUpdateAsync`, and `OpenExternalUri`.
- Produces: `ThemedDialogWindow.ShowBugFixUpdate` returning only `Primary` (update) or `Secondary` (later), with a non-closing GitHub link and no ignore action.

- [ ] **Step 1: Add failing dialog regression tests**

Add to `BugFixUpdateUiTests.cs`:

```csharp
[Fact]
public void ThemedDialog_HasScrollableBugFixDetailsAndNonClosingLink()
{
    var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ThemedDialogWindow.xaml"));
    var code = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ThemedDialogWindow.xaml.cs"));

    Assert.Contains("x:Name=\"DetailsScrollViewer\"", xaml, StringComparison.Ordinal);
    Assert.Contains("x:Name=\"DetailsBodyTextBlock\"", xaml, StringComparison.Ordinal);
    Assert.Contains("x:Name=\"DetailsLinkButton\"", xaml, StringComparison.Ordinal);
    Assert.Contains("ShowBugFixUpdate", code, StringComparison.Ordinal);
    var linkHandlerStart = code.IndexOf("private void OnDetailsLinkClicked", StringComparison.Ordinal);
    Assert.True(linkHandlerStart >= 0, "The details-link handler should exist.");
    var nextHandler = linkHandlerStart >= 0
        ? code.IndexOf("private void OnPrimaryClicked", linkHandlerStart, StringComparison.Ordinal)
        : -1;
    Assert.True(nextHandler > linkHandlerStart, "The primary handler should follow the details-link handler.");
    var linkHandler = code[linkHandlerStart..nextHandler];
    Assert.Contains("detailsLinkAction?.Invoke()", linkHandler, StringComparison.Ordinal);
    Assert.DoesNotContain("DialogResult", linkHandler, StringComparison.Ordinal);
    Assert.DoesNotContain("Close()", linkHandler, StringComparison.Ordinal);
}

[Fact]
public void StartupUpdateFlow_HandlesBugFixBeforeBetaWithoutIgnoreCall()
{
    var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml.cs"));
    var bugFixStart = source.IndexOf("if (availableUpdate.IsBugFix)", StringComparison.Ordinal);
    Assert.True(bugFixStart >= 0);
    var betaStart = bugFixStart >= 0
        ? source.IndexOf("if (availableUpdate.IsBeta)", bugFixStart, StringComparison.Ordinal)
        : -1;
    Assert.True(betaStart > bugFixStart);
    var bugFixBlock = source[bugFixStart..betaStart];
    Assert.Contains("ShowBugFixUpdate", bugFixBlock, StringComparison.Ordinal);
    Assert.Contains("StartApplicationSelfUpdateAsync", bugFixBlock, StringComparison.Ordinal);
    Assert.DoesNotContain("IgnoreApplicationUpdate", bugFixBlock, StringComparison.Ordinal);
    Assert.DoesNotContain("IgnoreBetaApplicationUpdatesUntilStable", bugFixBlock, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run UI tests and verify RED**

Run:

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~BugFixUpdateUiTests"
```

Expected: assertions fail because the details mode and startup branch do not exist.

- [ ] **Step 3: Extend ThemedDialogWindow with a dedicated details mode**

Add the details callback field:

```csharp
private readonly Action? detailsLinkAction;
```

Replace the constructor signature with this backward-compatible signature, then initialize the new controls after `FinePrintTextBlock`:

```csharp
private ThemedDialogWindow(
    AppTheme theme,
    string title,
    string message,
    string primaryButtonText,
    string? secondaryButtonText = null,
    string? tertiaryButtonText = null,
    string? finePrint = null,
    bool isNotice = false,
    string? detailsBody = null,
    string? detailsLinkText = null,
    Action? detailsLinkAction = null)
{
    InitializeComponent();
    if (isNotice)
    {
        IsNotice = true;
        HeadingFontSize = 28;
        BodyFontSize = 15;
        FinePrintFontSize = 13;
    }
    ThemeManager.ApplyToResources(Resources, theme);
    ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
    Closed += OnWindowClosed;
    Title = LocalizationService.Format("{0} | Crystal Relay", title);
    HeaderTextBlock.Text = title;
    WindowTitleTextBlock.Text = title;
    MessageTextBlock.Text = message;
    PrimaryButton.Content = primaryButtonText;
    FinePrintTextBlock.Text = finePrint ?? string.Empty;
    FinePrintTextBlock.Visibility = string.IsNullOrWhiteSpace(finePrint)
        ? Visibility.Collapsed
        : Visibility.Visible;

    this.detailsLinkAction = detailsLinkAction;
    DetailsBodyTextBlock.Text = detailsBody ?? string.Empty;
    DetailsScrollViewer.Visibility = string.IsNullOrWhiteSpace(detailsBody)
        ? Visibility.Collapsed
        : Visibility.Visible;
    DetailsLinkButton.Content = detailsLinkText ?? string.Empty;
    DetailsLinkButton.Visibility = detailsLinkAction is null || string.IsNullOrWhiteSpace(detailsLinkText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    if (!string.IsNullOrWhiteSpace(secondaryButtonText))
    {
        SecondaryButton.Content = secondaryButtonText;
        SecondaryButton.Visibility = Visibility.Visible;
    }

    if (!string.IsNullOrWhiteSpace(tertiaryButtonText))
    {
        TertiaryButton.Content = tertiaryButtonText;
        TertiaryButton.Visibility = Visibility.Visible;
    }
}
```

Add this static API:

```csharp
public static ThemedDialogChoice ShowBugFixUpdate(
    Window? owner,
    AppTheme theme,
    string heading,
    string releaseTitle,
    string releaseBody,
    string finePrint,
    string updateNowText,
    string laterText,
    string viewOnGitHubText,
    Action viewOnGitHub)
{
    ArgumentNullException.ThrowIfNull(viewOnGitHub);
    var dialog = new ThemedDialogWindow(
        theme,
        heading,
        releaseTitle,
        updateNowText,
        secondaryButtonText: laterText,
        finePrint: finePrint,
        isNotice: true,
        detailsBody: releaseBody,
        detailsLinkText: viewOnGitHubText,
        detailsLinkAction: viewOnGitHub)
    {
        Owner = owner,
        Width = 720,
        MinWidth = 640,
        MaxHeight = 760
    };

    return dialog.ShowDialog() == true
        ? ThemedDialogChoice.Primary
        : ThemedDialogChoice.Secondary;
}

private void OnDetailsLinkClicked(object sender, RoutedEventArgs e)
{
    try
    {
        detailsLinkAction?.Invoke();
    }
    catch
    {
    }
}
```

Replace the inner content grid's row definitions with:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
</Grid.RowDefinitions>
```

Insert these controls after the existing message border in row 2:

```xml
<ScrollViewer x:Name="DetailsScrollViewer"
              Grid.Row="3"
              Margin="4,14,4,0"
              MaxHeight="280"
              HorizontalScrollBarVisibility="Disabled"
              VerticalScrollBarVisibility="Auto"
              Visibility="Collapsed">
    <TextBlock x:Name="DetailsBodyTextBlock"
               Foreground="{DynamicResource TextBrush}"
               FontSize="{Binding BodyFontSize, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}"
               TextWrapping="Wrap" />
</ScrollViewer>

<Button x:Name="DetailsLinkButton"
        Grid.Row="4"
        Margin="4,10,4,0"
        Padding="12,7"
        HorizontalAlignment="Left"
        Style="{StaticResource SecondaryButtonStyle}"
        Visibility="Collapsed"
        Click="OnDetailsLinkClicked" />
```

Change `FinePrintTextBlock` to `Grid.Row="5"` and the action `WrapPanel` to `Grid.Row="6"`. Leave their other properties and all three existing action buttons unchanged.

- [ ] **Step 4: Add the mandatory Bug Fix branch before beta/stable dialogs**

In `CheckForApplicationUpdateAsync`, after self-update support is confirmed and before constructing the generic `updateMessage`, add:

```csharp
if (availableUpdate.IsBugFix)
{
    var releaseBody = string.IsNullOrWhiteSpace(availableUpdate.ReleaseBody)
        ? LocalizationService.Translate(
            "This Bug Fix Push does not include release notes. View the GitHub release page for details.")
        : availableUpdate.ReleaseBody;
    var choice = ThemedDialogWindow.ShowBugFixUpdate(
        this,
        viewModel.SelectedTheme,
        LocalizationService.Format(
            "Bug Fix Push {0} for Crystal Relay v{1}",
            availableUpdate.BugFixSequence,
            availableUpdate.LatestBaseVersion),
        availableUpdate.ReleaseTitle,
        releaseBody,
        LocalizationService.Translate(
            "This Bug Fix Push cannot be permanently skipped. Choose Later to be reminded the next time Crystal Relay starts."),
        LocalizationService.Translate("Update Now"),
        LocalizationService.Translate("Later"),
        LocalizationService.Translate("View on GitHub"),
        () => OpenExternalUri(availableUpdate.ReleasePageUrl));

    if (choice == ThemedDialogChoice.Primary)
    {
        await StartApplicationSelfUpdateAsync(availableUpdate, cancellationToken);
    }

    return;
}
```

Do not write any setting in this branch.

- [ ] **Step 5: Add exact English localization keys**

Add these key/value pairs to `en-US.extra.json`:

```json
"Bug Fix Push {0} for Crystal Relay v{1}": "Bug Fix Push {0} for Crystal Relay v{1}",
"Update Now": "Update Now",
"Later": "Later",
"View on GitHub": "View on GitHub",
"This Bug Fix Push cannot be permanently skipped. Choose Later to be reminded the next time Crystal Relay starts.": "This Bug Fix Push cannot be permanently skipped. Choose Later to be reminded the next time Crystal Relay starts.",
"This Bug Fix Push does not include release notes. View the GitHub release page for details.": "This Bug Fix Push does not include release notes. View the GitHub release page for details.",
"BUG FIX {0}": "BUG FIX {0}",
"Bug Fix {0}": "Bug Fix {0}"
```

- [ ] **Step 6: Add natural translations to every non-English extra file**

Use the same eight keys and these exact values, preserving `{0}` then `{1}` order:

| Locale | Push heading | Update Now | Later | View on GitHub | Cannot skip | Missing notes | Badge | Display label |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| de-DE | `Bugfix-Push {0} für Crystal Relay v{1}` | `Jetzt aktualisieren` | `Später` | `Auf GitHub ansehen` | `Dieser Bugfix-Push kann nicht dauerhaft übersprungen werden. Wähle „Später“, damit Crystal Relay dich beim nächsten Start wieder erinnert.` | `Für diesen Bugfix-Push sind keine Versionshinweise verfügbar. Details findest du auf der GitHub-Release-Seite.` | `BUGFIX {0}` | `Bugfix {0}` |
| es-ES | `Parche {0} para Crystal Relay v{1}` | `Actualizar ahora` | `Más tarde` | `Ver en GitHub` | `Este parche no se puede omitir para siempre. Elige Más tarde para que Crystal Relay te lo recuerde la próxima vez que se inicie.` | `Este parche no incluye notas de la versión. Consulta la página de la versión en GitHub para ver los detalles.` | `PARCHE {0}` | `Parche {0}` |
| fr-FR | `Correctif {0} pour Crystal Relay v{1}` | `Mettre à jour maintenant` | `Plus tard` | `Voir sur GitHub` | `Ce correctif ne peut pas être ignoré définitivement. Choisis Plus tard pour que Crystal Relay te le rappelle au prochain démarrage.` | `Ce correctif ne contient pas de notes de version. Consulte la page de la version sur GitHub pour plus de détails.` | `CORRECTIF {0}` | `Correctif {0}` |
| it-IT | `Correzione {0} per Crystal Relay v{1}` | `Aggiorna ora` | `Più tardi` | `Visualizza su GitHub` | `Questa correzione non può essere ignorata definitivamente. Scegli Più tardi per ricevere di nuovo l'avviso al prossimo avvio di Crystal Relay.` | `Questa correzione non include note di rilascio. Apri la pagina della release su GitHub per i dettagli.` | `CORREZIONE {0}` | `Correzione {0}` |
| pt-BR | `Correção {0} para o Crystal Relay v{1}` | `Atualizar agora` | `Mais tarde` | `Ver no GitHub` | `Esta correção não pode ser ignorada para sempre. Escolha Mais tarde para o Crystal Relay lembrar você na próxima inicialização.` | `Esta correção não inclui notas da versão. Veja os detalhes na página da versão no GitHub.` | `CORREÇÃO {0}` | `Correção {0}` |
| ru-RU | `Исправление {0} для Crystal Relay v{1}` | `Обновить сейчас` | `Позже` | `Открыть на GitHub` | `Это исправление нельзя пропустить навсегда. Выбери «Позже», и Crystal Relay напомнит о нём при следующем запуске.` | `Для этого исправления нет примечаний к выпуску. Подробности смотри на странице выпуска GitHub.` | `ИСПРАВЛЕНИЕ {0}` | `Исправление {0}` |
| ja-JP | `バグ修正アップデート {0} - Crystal Relay v{1}` | `今すぐ更新` | `後で` | `GitHub で見る` | `このバグ修正アップデートは完全にスキップできません。「後で」を選ぶと、次回 Crystal Relay を起動したときにもう一度お知らせします。` | `このバグ修正アップデートにはリリースノートがありません。詳しくは GitHub のリリースページをご覧ください。` | `バグ修正 {0}` | `バグ修正 {0}` |
| ko-KR | `버그 수정 업데이트 {0} - Crystal Relay v{1}` | `지금 업데이트` | `나중에` | `GitHub에서 보기` | `이 버그 수정 업데이트는 영구적으로 건너뛸 수 없습니다. 나중에를 선택하면 다음에 Crystal Relay를 시작할 때 다시 알려드립니다.` | `이 버그 수정 업데이트에는 릴리스 노트가 없습니다. 자세한 내용은 GitHub 릴리스 페이지에서 확인하세요.` | `버그 수정 {0}` | `버그 수정 {0}` |
| zh-CN | `错误修复更新 {0}，适用于 Crystal Relay v{1}` | `立即更新` | `稍后` | `在 GitHub 上查看` | `此错误修复更新无法永久跳过。选择“稍后”，Crystal Relay 会在下次启动时再次提醒你。` | `此错误修复更新没有附带发行说明。请前往 GitHub 发行页面查看详情。` | `错误修复 {0}` | `错误修复 {0}` |
| zh-TW | `錯誤修正更新 {0}，適用於 Crystal Relay v{1}` | `立即更新` | `稍後` | `在 GitHub 上查看` | `此錯誤修正更新無法永久略過。選擇「稍後」，Crystal Relay 會在下次啟動時再次提醒你。` | `此錯誤修正更新沒有附帶版本說明。請前往 GitHub 發行頁面查看詳情。` | `錯誤修正 {0}` | `錯誤修正 {0}` |
| th-TH | `อัปเดตแก้บั๊ก {0} สำหรับ Crystal Relay v{1}` | `อัปเดตตอนนี้` | `ไว้ภายหลัง` | `ดูบน GitHub` | `อัปเดตแก้บั๊กนี้ไม่สามารถข้ามแบบถาวรได้ เลือก ไว้ภายหลัง เพื่อให้ Crystal Relay แจ้งเตือนอีกครั้งเมื่อเปิดครั้งถัดไป` | `อัปเดตแก้บั๊กนี้ไม่มีบันทึกประจำรุ่น ดูรายละเอียดได้ที่หน้ารีลีสบน GitHub` | `แก้บั๊ก {0}` | `แก้บั๊ก {0}` |
| sv-SE | `Buggfix {0} för Crystal Relay v{1}` | `Uppdatera nu` | `Senare` | `Visa på GitHub` | `Den här buggfixen kan inte hoppas över permanent. Välj Senare så påminner Crystal Relay dig nästa gång programmet startar.` | `Den här buggfixen saknar versionsanteckningar. Se GitHub-utgåvan för mer information.` | `BUGGFIX {0}` | `Buggfix {0}` |
| pl-PL | `Poprawka {0} dla Crystal Relay v{1}` | `Aktualizuj teraz` | `Później` | `Zobacz na GitHubie` | `Tej poprawki nie można pominąć na stałe. Wybierz Później, a Crystal Relay przypomni ci przy następnym uruchomieniu.` | `Ta poprawka nie zawiera informacji o wydaniu. Szczegóły znajdziesz na stronie wydania w GitHubie.` | `POPRAWKA {0}` | `Poprawka {0}` |
| cs-CZ | `Oprava {0} pro Crystal Relay v{1}` | `Aktualizovat teď` | `Později` | `Zobrazit na GitHubu` | `Tuto opravu nejde trvale přeskočit. Zvol Později a Crystal Relay ti ji připomene při příštím spuštění.` | `Tato oprava nemá poznámky k vydání. Podrobnosti najdeš na stránce vydání na GitHubu.` | `OPRAVA {0}` | `Oprava {0}` |

- [ ] **Step 7: Run UI tests and localization audit**

Run:

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~BugFixUpdateUiTests"
dotnet run --project "LocalizationAudit\LocalizationAudit.csproj" --configuration Release -- "VrcTwitchOscBridge\Resources\Localization"
```

Expected: UI tests pass; localization audit reports no missing keys, empty values, or placeholder mismatches.

- [ ] **Step 8: Commit checkpoint only when explicitly authorized**

```powershell
git add -- "VrcTwitchOscBridge/ThemedDialogWindow.xaml" "VrcTwitchOscBridge/ThemedDialogWindow.xaml.cs" "VrcTwitchOscBridge/MainWindow.xaml.cs" "VrcTwitchOscBridge/Resources/Localization" "VrcTwitchOscBridge.Tests/BugFixUpdateUiTests.cs"
git commit -m "feat(update): show mandatory bug fix details"
```

Without explicit authorization, skip these commands.

---

### Task 7: Add The Bug Fix Build And Optional Publish Script

**Files:**
- Create: `Build-Crystal-Relay-BugFix.ps1`
- Modify: `tools/github/Export-Crystal-Relay-Public.ps1`
- Modify: `tools/github/Test-Crystal-Relay-PublicSafety.ps1`
- Test: `VrcTwitchOscBridge.Tests/BugFixBuildScriptTests.cs`

**Interfaces:**
- Consumes: stable base tag, exact changelog/release-record section, clean hotfix worktree, project version, localization audit, public/private repo status, `gh` authentication.
- Produces: a private maintainer script, flat full package and ZIP in `Releases\v<base>`, optional normal GitHub release with exact tag/title/notes/asset, and explicit public-export exclusion for the private workflow files.

- [ ] **Step 1: Write the failing script-contract test**

Create `VrcTwitchOscBridge.Tests/BugFixBuildScriptTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BugFixBuildScriptTests
{
    [Fact]
    public void Script_ContainsRequiredBuildAndPublishSafetyContract()
    {
        var script = File.ReadAllText(GetScriptPath());

        Assert.Contains("[int]$BugFix", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$Publish", script, StringComparison.Ordinal);
        Assert.Contains("CrystalRelayBugFix-v$targetVersion-$bugFixName-$runtime", script, StringComparison.Ordinal);
        Assert.Contains("channel = 'bugfix'", script, StringComparison.Ordinal);
        Assert.Contains("bugfix-build.flag", script, StringComparison.Ordinal);
        Assert.Contains("CrystalRelayTwitchOsc-v$targetVersion-$bugFixName.exe", script, StringComparison.Ordinal);
        Assert.Contains("Assert-SafeBuildPath", script, StringComparison.Ordinal);
        Assert.Contains("git -C $root merge-base --is-ancestor", script, StringComparison.Ordinal);
        Assert.Contains("Active build lane: `bugfix`", script, StringComparison.Ordinal);
        Assert.Contains("Active bug fix push:", script, StringComparison.Ordinal);
        Assert.Contains("Assert-RepoCleanAndPushed", script, StringComparison.Ordinal);
        Assert.Contains("Test-Crystal-Relay-PublicSafety.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("$asset.digest", script, StringComparison.Ordinal);
        Assert.Contains("$expectedDigest", script, StringComparison.Ordinal);
        Assert.Contains("dotnet test $testProjectPath", script, StringComparison.Ordinal);
        Assert.Contains("gh release create", script, StringComparison.Ordinal);
        Assert.Contains("--latest", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--prerelease", script, StringComparison.Ordinal);
        Assert.DoesNotContain("crystal-relay-private", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git commit", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git push", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_ExtractsExactAnchoredChangelogSection()
    {
        var script = File.ReadAllText(GetScriptPath());
        Assert.Contains("Get-ChangelogSection", script, StringComparison.Ordinal);
        Assert.Contains("^v\\d+\\.\\d+\\.\\d+(?:\\s|$)", script, StringComparison.Ordinal);
        Assert.Contains("[regex]::Escape($Header)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesExactBaseVersionWithoutDecimalRollover()
    {
        var script = File.ReadAllText(GetScriptPath());
        Assert.Contains("'^\\d+\\.\\d+\\.\\d+$'", script, StringComparison.Ordinal);
        Assert.Contains("$targetVersion = $Version.Trim()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Normalize-VersionText", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-VersionText", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintainerScript_IsBlockedFromPublicExport()
    {
        var root = GetRepoRoot();
        var exportScript = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "github",
            "Export-Crystal-Relay-Public.ps1"));
        var safetyScript = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "github",
            "Test-Crystal-Relay-PublicSafety.ps1"));

        Assert.Contains("'Build-Crystal-Relay-BugFix.ps1'", exportScript, StringComparison.Ordinal);
        Assert.Contains("'Build-Crystal-Relay-BugFix.ps1'", safetyScript, StringComparison.Ordinal);
        Assert.Contains("'BugFixBuildScriptTests.cs'", exportScript, StringComparison.Ordinal);
        Assert.Contains("'BugFixBuildScriptTests.cs'", safetyScript, StringComparison.Ordinal);
    }

    private static string GetScriptPath([CallerFilePath] string testPath = "")
    {
        return Path.Combine(GetRepoRoot(testPath), "Build-Crystal-Relay-BugFix.ps1");
    }

    private static string GetRepoRoot([CallerFilePath] string testPath = "") =>
        Directory.GetParent(Path.GetDirectoryName(testPath)!)!.FullName;
}
```

- [ ] **Step 2: Run script tests and verify RED**

Run:

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~BugFixBuildScriptTests"
```

Expected: test failure because the script does not exist.

- [ ] **Step 3: Create the complete standalone build/publish script**

Create `Build-Crystal-Relay-BugFix.ps1` with this complete content. Unlike the stable/beta scripts, it validates the literal three-part base and never normalizes or rewrites it, so approved base `3.1.10` remains `3.1.10`:

```powershell
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$BugFix,

    [switch]$Publish,

    [string]$PrivateRepoPath,

    [string]$PublicRepoPath = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GitHub\crystal-relay-public')
)

$ErrorActionPreference = 'Stop'

function Get-VersionText {
    param([xml]$ProjectXml)

    $versionNode = $ProjectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($versionNode)) {
        throw 'Could not find a <Version> node in the project file.'
    }

    return $versionNode.Trim()
}

function Test-SemanticVersion {
    param([string]$VersionText)

    return $VersionText -match '^\d+\.\d+\.\d+$'
}

function Assert-SafeBuildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RequiredParent,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullParent = [System.IO.Path]::GetFullPath($RequiredParent).TrimEnd('\', '/')
    $parentPrefix = $fullParent + [System.IO.Path]::DirectorySeparatorChar
    if ($full -eq $fullParent -or -not $full.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove '$Path': it is not a child of '$RequiredParent'."
    }

    $leaf = Split-Path -Leaf $full
    if ($leaf -notlike $Pattern) {
        throw "Refusing to remove '$Path': name '$leaf' does not match '$Pattern'."
    }

    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    if ($full.StartsWith($localAppData, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove '$Path': it is under LocalAppData."
    }
}

function Test-FileHasExactLine {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Line
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $text = Get-Content -LiteralPath $Path -Raw
    $pattern = '^' + [regex]::Escape($Line) + '\s*$'
    return [regex]::IsMatch(
        $text,
        $pattern,
        [System.Text.RegularExpressions.RegexOptions]::Multiline)
}

function Get-ChangelogSection {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Header
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required changelog not found: $Path"
    }

    $lines = @(Get-Content -LiteralPath $Path)
    $headerPattern = '^' + [regex]::Escape($Header) + '\s*$'
    $nextReleasePattern = '^v\d+\.\d+\.\d+(?:\s|$)'
    $start = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match $headerPattern) {
            $start = $index
            break
        }
    }

    if ($start -lt 0) {
        throw "CHANGELOG.txt is missing exact section '$Header'."
    }

    $section = New-Object System.Collections.Generic.List[string]
    for ($index = $start; $index -lt $lines.Count; $index++) {
        if ($index -gt $start -and $lines[$index] -match $nextReleasePattern) {
            break
        }
        $section.Add($lines[$index])
    }

    $text = ($section -join [Environment]::NewLine).Trim()
    if ($text -notmatch '(?m)^-\s+\S') {
        throw "CHANGELOG.txt section '$Header' must include at least one user-facing bullet."
    }
    return $text
}

function Test-RecordBaselineMatches {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$VersionText
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $text = Get-Content -LiteralPath $Path -Raw
    $pattern = 'Current working source version:\s*v?' + [regex]::Escape($VersionText) + '(?:\b|$)'
    return [regex]::IsMatch($text, $pattern)
}

function Test-WorkingTreeClean {
    param([Parameter(Mandatory = $true)][string]$Path)

    $status = @(& git -C $Path status --porcelain 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return $false
    }
    return $status.Count -eq 0
}

function Assert-RepoCleanAndPushed {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        throw "$Label repository path was not found: $Path"
    }

    $insideWorkTree = (& git -C $Path rev-parse --is-inside-work-tree 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $insideWorkTree -ne 'true') {
        throw "$Label path is not a Git working tree: $Path"
    }

    $status = @(& git -C $Path status --porcelain 2>$null)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
        throw "$Label repository must be clean before publication."
    }

    $upstream = (& git -C $Path rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($upstream)) {
        throw "$Label repository does not have an upstream branch."
    }

    $head = (& git -C $Path rev-parse HEAD 2>$null | Out-String).Trim()
    $upstreamHead = (& git -C $Path rev-parse '@{u}' 2>$null | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($head) -or $head -ne $upstreamHead) {
        throw "$Label repository HEAD is not synchronized with $upstream."
    }

    return $head
}

function Assert-PackageShape {
    param(
        [Parameter(Mandatory = $true)][string]$PackageDirectory,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$ExpectedEntryExecutable,
        [Parameter(Mandatory = $true)][string]$ExpectedMarker
    )

    $applicationExecutables = @(
        Get-ChildItem -LiteralPath $PackageDirectory -File -Filter '*.exe' |
            Where-Object {
                $_.Name -eq 'Crystal Relay.exe' -or
                $_.Name -like 'CrystalRelayTwitchOsc-v*.exe'
            }
    )
    if ($applicationExecutables.Count -ne 1 -or $applicationExecutables[0].Name -cne $ExpectedEntryExecutable) {
        throw "Bug Fix package must contain exactly one app executable named '$ExpectedEntryExecutable'."
    }

    foreach ($requiredFile in @(
        'CrystalRelayUpdater.exe',
        'crystal-relay-update.json',
        'bugfix-build.flag',
        'README.md',
        'CHANGELOG.txt'
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $PackageDirectory $requiredFile) -PathType Leaf)) {
            throw "Bug Fix package is missing '$requiredFile'."
        }
    }

    $manifestPath = Join-Path $PackageDirectory 'crystal-relay-update.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.productName -cne 'Crystal Relay' -or
        $manifest.version -cne $ExpectedVersion -or
        $manifest.channel -cne 'bugfix' -or
        $manifest.runtime -cne 'win-x64' -or
        $manifest.entryExecutableName -cne $ExpectedEntryExecutable) {
        throw 'Bug Fix package manifest does not match the requested identity.'
    }

    $marker = Get-Content -LiteralPath (Join-Path $PackageDirectory 'bugfix-build.flag') -Raw
    if ($marker -cne $ExpectedMarker) {
        throw "bugfix-build.flag must contain exactly '$ExpectedMarker'."
    }
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'VrcTwitchOscBridge\VrcTwitchOscBridge.csproj'
$updaterProjectPath = Join-Path $root 'CrystalRelayUpdater\CrystalRelayUpdater.csproj'
$testProjectPath = Join-Path $root 'VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj'
$localizationAuditProject = Join-Path $root 'LocalizationAudit\LocalizationAudit.csproj'
$localizationRoot = Join-Path $root 'VrcTwitchOscBridge\Resources\Localization'
$agentsPath = Join-Path $root 'AGENTS.md'
$readmePath = Join-Path $root 'README.md'
$changelogPath = Join-Path $root 'CHANGELOG.txt'
$releaseRecordPath = Join-Path $root 'RELEASE-CHANGE-RECORD.txt'
$docsPath = Join-Path $root 'docs'
$publicSafetyScript = Join-Path $root 'tools\github\Test-Crystal-Relay-PublicSafety.ps1'
$releaseRoot = Join-Path $root 'Releases'
$publishConfig = 'Release'
$runtime = 'win-x64'

$targetVersion = $Version.Trim()
if (-not (Test-SemanticVersion -VersionText $targetVersion)) {
    throw 'Version must use the exact major.minor.patch form, such as 3.1.10.'
}

$bugFixName = "bugfix$BugFix"
$bugFixLabel = "Bug Fix Push $BugFix"
$bugFixIdentity = "$targetVersion-$bugFixName"
$bugFixHeader = "v$targetVersion bug fix $BugFix"
$stableTag = "v$targetVersion"
$tag = "v$bugFixIdentity"
$releaseTitle = "Crystal Relay v$targetVersion Bug Fix Push $BugFix"
$versionRoot = Join-Path $releaseRoot "v$targetVersion"
$releaseName = "CrystalRelayBugFix-v$targetVersion-$bugFixName-$runtime"
$publishDir = Join-Path $versionRoot $releaseName
$zipPath = Join-Path $versionRoot "$releaseName.zip"
$targetExeName = "CrystalRelayTwitchOsc-v$targetVersion-$bugFixName.exe"

[xml]$projectXml = Get-Content -LiteralPath $projectPath
[xml]$updaterProjectXml = Get-Content -LiteralPath $updaterProjectPath
$currentVersion = Get-VersionText -ProjectXml $projectXml
$updaterCurrentVersion = Get-VersionText -ProjectXml $updaterProjectXml
if ($currentVersion -ne $targetVersion -or $updaterCurrentVersion -ne $targetVersion) {
    throw "Bug Fix Pushes must keep both project versions at the stable base $targetVersion."
}

if (-not (Test-FileHasExactLine -Path $agentsPath -Line '- Active build lane: `bugfix`')) {
    throw 'AGENTS.md must set Active build lane to `bugfix` before building.'
}
$expectedActiveBugFixLine = "- Active bug fix push: ``v$bugFixIdentity``"
if (-not (Test-FileHasExactLine -Path $agentsPath -Line $expectedActiveBugFixLine)) {
    throw "AGENTS.md must set Active bug fix push to `v$bugFixIdentity` before building."
}
if (-not (Test-FileHasExactLine -Path $changelogPath -Line $bugFixHeader)) {
    throw "CHANGELOG.txt is missing exact section '$bugFixHeader'."
}
if (-not (Test-RecordBaselineMatches -Path $releaseRecordPath -VersionText $targetVersion)) {
    throw "RELEASE-CHANGE-RECORD.txt Current working source version must match $targetVersion."
}
if (-not (Test-FileHasExactLine -Path $releaseRecordPath -Line $bugFixHeader)) {
    throw "RELEASE-CHANGE-RECORD.txt must mirror exact heading '$bugFixHeader'."
}
$sourceNotes = Get-ChangelogSection -Path $changelogPath -Header $bugFixHeader

if ($env:CR_SKIP_GIT_CHECK -ne '1' -and -not (Test-WorkingTreeClean -Path $root)) {
    throw 'Refusing to build with a dirty working tree. Commit the isolated hotfix or set CR_SKIP_GIT_CHECK=1 for local package testing only.'
}

& git -C $root rev-parse --verify "refs/tags/$stableTag" *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Stable tag '$stableTag' is required before building a Bug Fix Push."
}
& git -C $root merge-base --is-ancestor $stableTag HEAD
if ($LASTEXITCODE -ne 0) {
    throw "The current hotfix source is not based on stable tag '$stableTag'."
}
$sourceCommit = (& git -C $root rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
    throw 'Could not determine the hotfix source commit.'
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null
if (Test-Path -LiteralPath $publishDir) {
    Assert-SafeBuildPath -Path $publishDir -RequiredParent $versionRoot -Pattern $releaseName
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.nuget\http-cache'
$env:APPDATA = Join-Path $root '.appdata'
$env:HOME = $root
New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $env:APPDATA 'NuGet') -Force | Out-Null

& dotnet run --project $localizationAuditProject --configuration Release -- $localizationRoot
if ($LASTEXITCODE -ne 0) {
    throw 'Localization audit failed.'
}

& dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw 'Crystal Relay release build failed.'
}
& dotnet build $updaterProjectPath --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw 'Crystal Relay updater release build failed.'
}

$bugFixTestFilter = 'FullyQualifiedName~ApplicationUpdatePackageRulesTests|FullyQualifiedName~ApplicationBuildIdentityTests|FullyQualifiedName~ApplicationUpdateServiceTests|FullyQualifiedName~ApplicationSelfUpdateBugFixTests|FullyQualifiedName~BugFixUpdateUiTests|FullyQualifiedName~BugFixBuildScriptTests'
& dotnet test $testProjectPath --configuration Release --filter $bugFixTestFilter
if ($LASTEXITCODE -ne 0) {
    throw 'Targeted Bug Fix update tests failed.'
}

& dotnet publish $projectPath `
    --configuration $publishConfig `
    --runtime $runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishTrimmed=false `
    --output $publishDir `
    --configfile (Join-Path $root 'NuGet.Config')
if ($LASTEXITCODE -ne 0) {
    throw 'Crystal Relay Bug Fix publish failed.'
}

$defaultExe = Join-Path $publishDir 'CrystalRelayTwitchOsc.exe'
if (-not (Test-Path -LiteralPath $defaultExe -PathType Leaf)) {
    throw 'Published Crystal Relay executable was not found.'
}
Rename-Item -LiteralPath $defaultExe -NewName $targetExeName -Force

$updaterPublishDir = Join-Path ([System.IO.Path]::GetTempPath()) ("CrystalRelayUpdater-" + [guid]::NewGuid().ToString('N'))
try {
    & dotnet publish $updaterProjectPath `
        --configuration $publishConfig `
        --runtime $runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishTrimmed=true `
        --output $updaterPublishDir `
        --configfile (Join-Path $root 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) {
        throw 'Crystal Relay updater publish failed.'
    }

    $updaterExe = Join-Path $updaterPublishDir 'CrystalRelayUpdater.exe'
    if (-not (Test-Path -LiteralPath $updaterExe -PathType Leaf)) {
        throw 'Published Crystal Relay updater executable was not found.'
    }
    Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $publishDir 'CrystalRelayUpdater.exe') -Force
}
finally {
    if (Test-Path -LiteralPath $updaterPublishDir) {
        Assert-SafeBuildPath `
            -Path $updaterPublishDir `
            -RequiredParent ([System.IO.Path]::GetTempPath()) `
            -Pattern 'CrystalRelayUpdater-*'
        Remove-Item -LiteralPath $updaterPublishDir -Recurse -Force
    }
}

$bugFixName | Set-Content -LiteralPath (Join-Path $publishDir 'bugfix-build.flag') -NoNewline -Encoding UTF8
New-Item -LiteralPath (Join-Path $publishDir "$bugFixIdentity.txt") -ItemType File -Force | Out-Null
Copy-Item -LiteralPath $readmePath -Destination (Join-Path $publishDir 'README.md') -Force
Copy-Item -LiteralPath $changelogPath -Destination (Join-Path $publishDir 'CHANGELOG.txt') -Force
if (Test-Path -LiteralPath $docsPath) {
    $packagedDocsPath = Join-Path $publishDir 'docs'
    Copy-Item -LiteralPath $docsPath -Destination $packagedDocsPath -Recurse -Force
    $internalDocsPath = Join-Path $packagedDocsPath 'superpowers'
    if (Test-Path -LiteralPath $internalDocsPath) {
        Assert-SafeBuildPath -Path $internalDocsPath -RequiredParent $packagedDocsPath -Pattern 'superpowers'
        Remove-Item -LiteralPath $internalDocsPath -Recurse -Force
    }
}

$updateManifest = [ordered]@{
    productName = 'Crystal Relay'
    version = $bugFixIdentity
    channel = 'bugfix'
    runtime = $runtime
    entryExecutableName = $targetExeName
}
$updateManifest |
    ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $publishDir 'crystal-relay-update.json') -Encoding UTF8

Assert-PackageShape `
    -PackageDirectory $publishDir `
    -ExpectedVersion $bugFixIdentity `
    -ExpectedEntryExecutable $targetExeName `
    -ExpectedMarker $bugFixName

Compress-Archive `
    -Path (Join-Path $publishDir '*') `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal

$zipInfo = Get-Item -LiteralPath $zipPath
if ($zipInfo.Length -le 0) {
    throw 'Bug Fix ZIP was not created correctly.'
}
$localSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host "Version:       $targetVersion"
Write-Host "Bug Fix:       $bugFixLabel"
Write-Host "Source commit: $sourceCommit"
Write-Host "Folder:        $publishDir"
Write-Host "ZIP:           $zipPath"
Write-Host "SHA-256:       $localSha256"

if (-not $Publish) {
    Write-Host 'Package complete. GitHub publication was not requested.'
    return
}

if ([string]::IsNullOrWhiteSpace($PrivateRepoPath)) {
    throw '-PrivateRepoPath is required with -Publish.'
}

$privateHead = Assert-RepoCleanAndPushed -Path $PrivateRepoPath -Label 'Private'
$publicHead = Assert-RepoCleanAndPushed -Path $PublicRepoPath -Label 'Public'
$privateNotes = Get-ChangelogSection `
    -Path (Join-Path $PrivateRepoPath 'CHANGELOG.txt') `
    -Header $bugFixHeader
$publicNotes = Get-ChangelogSection `
    -Path (Join-Path $PublicRepoPath 'CHANGELOG.txt') `
    -Header $bugFixHeader
if ($sourceNotes -cne $privateNotes -or $sourceNotes -cne $publicNotes) {
    throw 'Source, private, and public CHANGELOG Bug Fix sections are not identical.'
}

if (-not (Test-Path -LiteralPath $publicSafetyScript -PathType Leaf)) {
    throw "Public safety preflight was not found: $publicSafetyScript"
}
& powershell -NoProfile -ExecutionPolicy Bypass -File $publicSafetyScript -PublicRepoPath $PublicRepoPath
if ($LASTEXITCODE -ne 0) {
    throw 'Public safety preflight failed.'
}

& gh auth status --hostname github.com
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI authentication is required before publication.'
}

$remoteTag = @(& git -C $PublicRepoPath ls-remote --tags origin "refs/tags/$tag" 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not verify remote Git tags.'
}
if ($remoteTag.Count -ne 0) {
    throw "Remote tag '$tag' already exists."
}

$existingReleaseOutput = (& gh api "repos/seluvia/crystal-relay-public/releases/tags/$tag" 2>&1 | Out-String)
$existingReleaseExitCode = $LASTEXITCODE
if ($existingReleaseExitCode -eq 0) {
    throw "GitHub release '$tag' already exists."
}
if ($existingReleaseOutput -notmatch 'HTTP 404') {
    throw "Could not prove that GitHub release '$tag' is absent: $existingReleaseOutput"
}

$notesPath = [System.IO.Path]::GetTempFileName()
try {
    $sourceNotes | Set-Content -LiteralPath $notesPath -Encoding UTF8

    & gh release create $tag $zipPath `
        --repo 'seluvia/crystal-relay-public' `
        --target $publicHead `
        --title $releaseTitle `
        --notes-file $notesPath `
        --latest
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub Bug Fix release creation failed. Check GitHub for partial state before retrying.'
    }

    $expectedDigest = "sha256:$localSha256"
    $verified = $false
    $verificationError = 'GitHub did not return the published release.'
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        $releaseJson = (& gh api "repos/seluvia/crystal-relay-public/releases/tags/$tag" 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            $verificationError = "GitHub release query failed: $releaseJson"
        }
        else {
            $release = $releaseJson | ConvertFrom-Json
            $matchingAssets = @($release.assets | Where-Object { $_.name -ceq $zipInfo.Name })
            if ($release.tag_name -cne $tag) {
                $verificationError = 'Published tag does not match.'
            }
            elseif ($release.name -cne $releaseTitle) {
                $verificationError = 'Published title does not match.'
            }
            elseif ([bool]$release.draft -or [bool]$release.prerelease) {
                $verificationError = 'Published release is draft or prerelease.'
            }
            elseif (@($release.assets).Count -ne 1 -or $matchingAssets.Count -ne 1) {
                $verificationError = 'Published release does not contain exactly the expected asset.'
            }
            else {
                $asset = $matchingAssets[0]
                if ([long]$asset.size -ne [long]$zipInfo.Length) {
                    $verificationError = 'Published asset size does not match the local ZIP.'
                }
                elseif ([string]::IsNullOrWhiteSpace([string]$asset.digest)) {
                    $verificationError = 'GitHub has not populated the asset digest yet.'
                }
                else {
                    $remoteDigest = ([string]$asset.digest).ToLowerInvariant()
                    if ($remoteDigest -cne $expectedDigest) {
                        $verificationError = 'Published asset digest does not match the local SHA-256.'
                    }
                    else {
                        $verified = $true
                        break
                    }
                }
            }
        }

        if ($attempt -lt 6) {
            Start-Sleep -Seconds 5
        }
    }

    if (-not $verified) {
        throw "GitHub release '$tag' was created but verification failed: $verificationError Do not overwrite or delete it automatically."
    }
}
finally {
    if (Test-Path -LiteralPath $notesPath) {
        Remove-Item -LiteralPath $notesPath -Force
    }
}

Write-Host "Published and verified GitHub Bug Fix release $tag at public commit $publicHead."
Write-Host "Verified private source commit $privateHead before publication."
```

- [ ] **Step 4: Keep private maintainer workflow out of public export**

Add this exact entry to `$excludedFiles` in `tools/github/Export-Crystal-Relay-Public.ps1`:

```powershell
    'Build-Crystal-Relay-BugFix.ps1',
    'BugFixBuildScriptTests.cs',
```

Add the same exact entries to `-BlockedFiles` in `tools/github/Test-Crystal-Relay-PublicSafety.ps1`:

```powershell
        'Build-Crystal-Relay-BugFix.ps1',
        'BugFixBuildScriptTests.cs',
```

The script and its contract test contain internal source-isolation and repository-publication gates, including an `AGENTS.md` check. Excluding and blocking both preserves the existing public-content rules and keeps the public test project from referencing intentionally absent private tooling, without weakening or evading any blocked-content pattern.

- [ ] **Step 5: Parse-check the PowerShell script**

Run:

```powershell
powershell -NoProfile -Command "$errors = $null; [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path '.\Build-Crystal-Relay-BugFix.ps1'), [ref]$null, [ref]$errors) | Out-Null; if ($errors.Count) { $errors | Format-List | Out-String | Write-Error; exit 1 }"
```

Expected: exit code `0`, no parser errors.

- [ ] **Step 6: Run script-contract tests and verify GREEN**

Run the Task 7 test command.

Expected: all four script contract tests pass.

- [ ] **Step 7: Commit checkpoint only when explicitly authorized**

```powershell
git add -- "Build-Crystal-Relay-BugFix.ps1" "tools/github/Export-Crystal-Relay-Public.ps1" "tools/github/Test-Crystal-Relay-PublicSafety.ps1" "VrcTwitchOscBridge.Tests/BugFixBuildScriptTests.cs"
git commit -m "feat(release): add bug fix build and publish lane"
```

Without explicit authorization, skip these commands.

---

### Task 8: Document The Four Release Lanes

**Files:**
- Modify: `AGENTS.md`
- Modify: `RELEASE-CHANGE-RECORD.txt`
- Reference: `docs/superpowers/specs/2026-07-19-bug-fix-push-channel-design.md`

**Interfaces:**
- Consumes: final runtime/package/script names from Tasks 2-7.
- Produces: unambiguous maintainer workflow for stable, beta, test, and Bug Fix builds.

- [ ] **Step 1: Add Project Identity and canonical script entries**

Add:

```text
- Active bug fix push: `none`
```

Add this canonical path after the beta script:

```text
- Bug Fix release script:
  `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-BugFix.ps1`
```

Add these exact Versioning Rules bullets next to the existing active-build rules:

```text
- `Active build lane` accepts only `none`, `test`, `beta`, or `bugfix`.
- `Active bug fix push` is `none` outside an active Bug Fix build, otherwise it is the exact identity `v<version>-bugfix<N>`.
- Before a Bug Fix package is built, set `Active build lane` to `bugfix` and `Active bug fix push` to the exact package identity. After publication, reset both to `none` without changing the normal next-development version.
```

- [ ] **Step 2: Add an exact Bug Fix changelog workflow**

Add this exact section after the beta-cycle workflow and before stable rollup:

```markdown
### Bug Fix Push cycle
- A Bug Fix Push targets one already-published stable base version and never targets a beta or test build.
- Add a new public section at the top of `CHANGELOG.txt` using the exact heading `v<version> bug fix <N>`.
- Describe only changes since the previous stable or Bug Fix package for that base. Each later Bug Fix Push is cumulative and includes all earlier Bug Fix Push changes for the same base.
- Keep earlier Bug Fix sections in `CHANGELOG.txt` as public history and mirror every section into `RELEASE-CHANGE-RECORD.txt`.
- Publish the complete matching changelog section as the normal GitHub release notes. Bug Fix releases are not prereleases.
- The next stable changelog rolls up every surviving Bug Fix change into its fresh stable summary without deleting prior Bug Fix entries.
- Bug Fix Pushes do not update README stable highlights or the Void Crystal website download URL.
```

- [ ] **Step 3: Add exact build/package/publication rules**

Add this exact top-level rules section after the existing Build and Release Rules:

```markdown
## Bug Fix Push Rules
- Bug Fix Pushes are complete production packages for one exact published stable base. They never target beta or test builds, and the project/assembly base version remains unchanged.
- Build every Bug Fix Push from an isolated hotfix branch/worktree rooted at stable tag `v<version>`. Apply only the approved fixes and their tests; unfinished development work must not enter the package.
- Package identity is fixed:
  Tag: `v<version>-bugfix<N>`
  Title: `Crystal Relay v<version> Bug Fix Push <N>`
  Changelog heading: `v<version> bug fix <N>`
  Asset: `CrystalRelayBugFix-v<version>-bugfix<N>-win-x64.zip`
  Executable: `CrystalRelayTwitchOsc-v<version>-bugfix<N>.exe`
  Manifest: `version: <version>-bugfix<N>`, `channel: bugfix`
  Marker: `bugfix-build.flag` containing exactly `bugfix<N>`
  Output folder: `Releases\v<version>`
- Every later Bug Fix Push for a stable base is cumulative; sequence numbers are positive integers and normally increase.
- Before building, set `Active build lane: bugfix` and `Active bug fix push: v<version>-bugfix<N>` in this file and add matching changelog and private release-record sections.
- Build without publication using:
  `powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-BugFix.ps1" -Version <version> -BugFix <N>`
- Run direct verification or a test build first. Inspect the flat package, manifest, updater, marker, versioned executable, and in-place update behavior.
- Update, commit, push, and verify the private repository first. Then export, safety-check, commit, push, and verify the public repository.
- `-Publish` only publishes the already-built GitHub release; it never commits or pushes source. Use it only after explicit user authorization and all repository/public-safety prerequisites pass:
  `powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-BugFix.ps1" -Version <version> -BugFix <N> -Publish -PrivateRepoPath <path>`
- `Build-Crystal-Relay-BugFix.ps1` and its contract test contain private maintainer workflow checks. Keep both blocked from public export; do not weaken public-safety content rules to publish them.
- After successful publication, reset `Active build lane` and `Active bug fix push` to `none` while preserving the normal next-development version.
- Never create or publish a Bug Fix Push without explicit user instruction. Never change the website's pinned stable download URL for test, beta, or Bug Fix packages.
```

Replace the existing pre-flight block from `All three build scripts enforce` through the `CR_SKIP_GIT_CHECK` sentence with:

```text
- Stable, beta, test, and Bug Fix build scripts enforce pre-flight gates before publishing. A build refuses to run if:
  - `CHANGELOG.txt` lacks the expected `v<version>` section (release), `v<version> beta <N>` section (beta), either form (test), or exact `v<version> bug fix <N>` section (Bug Fix).
  - `RELEASE-CHANGE-RECORD.txt` does not match the source version policy for that lane. Test/beta retain their current warning behavior; release and Bug Fix builds throw.
  - The working tree has uncommitted changes, unless `CR_SKIP_GIT_CHECK=1` is used for local package testing.
  - A recursive removal path is outside the expected package/temp parent or fails its Crystal Relay shape check.
  - A Bug Fix build lacks the matching stable tag ancestry, exact active Bug Fix identity, exact release-record heading, or unchanged app/updater base versions.
- `CR_SKIP_GIT_CHECK=1` overrides only the working-tree gate. It never disables changelog, release-record, ancestry, identity, version, package-shape, or path-safety checks.
```

Replace `Release, beta, and test build scripts run the localization audit before publishing.` with:

```text
- Release, beta, test, and Bug Fix package scripts run the localization audit before publishing files.
```

Replace `Release and beta packages should include` with:

```text
- Release, beta, and Bug Fix packages include `CrystalRelayUpdater.exe` and a valid `crystal-relay-update.json` manifest. Bug Fix packages additionally include the exact `bugfix-build.flag` marker.
```

Replace the `All three build scripts rewrite` bullet with:

```text
- The release, beta, and test scripts retain their existing project-version rewrite behavior so the project file stays their source of truth.
- `Build-Crystal-Relay-BugFix.ps1` never rewrites project versions; it refuses to build unless the app and updater already equal the requested stable base.
```

In the website-download housekeeping paragraph, replace the final sentence with:

```text
Beta, test, and Bug Fix builds must NOT change the website download URL; it always tracks the most recent stable release only.
```

- [ ] **Step 4: Update the working release record**

Under the `v3.1.10` pending draft, add an `Added:` bullet:

```text
- Added an optional maintainer Bug Fix Push release lane that can deliver mandatory, fully described fixes to one stable Crystal Relay version through the existing secure self-updater without changing the normal stable version number.
```

Do not add an actual `v3.1.10 bug fix 1` changelog section now; that section is created only when a real post-stable Bug Fix Push is prepared.

- [ ] **Step 5: Verify documentation consistency**

Run:

```powershell
rg -n "bugfix|Bug Fix Push|Active bug fix push|Build-Crystal-Relay-BugFix" "AGENTS.md" "RELEASE-CHANGE-RECORD.txt" "docs/superpowers/specs/2026-07-19-bug-fix-push-channel-design.md" "docs/superpowers/plans/2026-07-19-bug-fix-push-channel.md"
```

Expected: one consistent tag, asset, manifest, marker, changelog, and command convention throughout.

- [ ] **Step 6: Commit checkpoint only when explicitly authorized**

```powershell
git add -- "AGENTS.md" "RELEASE-CHANGE-RECORD.txt" "docs/superpowers/specs/2026-07-19-bug-fix-push-channel-design.md" "docs/superpowers/plans/2026-07-19-bug-fix-push-channel.md"
git commit -m "docs: define bug fix push workflow"
```

Without explicit authorization, skip these commands.

---

### Task 9: Full Verification And Operational Gate

**Files:**
- Verify all files from Tasks 1-8.
- Do not modify public/private GitHub working copies or the website.

**Interfaces:**
- Consumes: completed runtime, UI, updater, localization, script, and documentation changes.
- Produces: evidence that the implementation is ready for a `v3.1.10` test build and that actual Bug Fix packaging is gated until `v3.1.10` is stable.

- [ ] **Step 1: Run all targeted Bug Fix tests**

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ApplicationUpdatePackageRulesTests|FullyQualifiedName~ApplicationBuildIdentityTests|FullyQualifiedName~ApplicationUpdateServiceTests|FullyQualifiedName~ApplicationSelfUpdateBugFixTests|FullyQualifiedName~BugFixUpdateUiTests|FullyQualifiedName~BugFixBuildScriptTests"
```

Expected: all targeted tests pass.

- [ ] **Step 2: Run the complete app test project**

```powershell
dotnet test "VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: no new failures. Any existing unrelated failures must be listed with proof that they reproduce without the Bug Fix Push diff; do not silently report the full suite as passing.

- [ ] **Step 3: Run localization audit**

```powershell
dotnet run --project "LocalizationAudit\LocalizationAudit.csproj" --configuration Release -- "VrcTwitchOscBridge\Resources\Localization"
```

Expected: audit passes for coverage, non-empty values, and exact placeholders.

- [ ] **Step 4: Build app and updater directly**

```powershell
dotnet build "VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
dotnet build "CrystalRelayUpdater\CrystalRelayUpdater.csproj" --no-restore
```

Expected: both builds succeed.

- [ ] **Step 5: Parse-check the build script and inspect diffs**

```powershell
powershell -NoProfile -Command "$errors = $null; [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path '.\Build-Crystal-Relay-BugFix.ps1'), [ref]$null, [ref]$errors) | Out-Null; if ($errors.Count) { exit 1 }"
git diff --check
git status --short
```

Expected: no parser or whitespace errors; status contains only intended files plus pre-existing unrelated worktree changes.

- [ ] **Step 6: Manually inspect the Debug UI when authorized to launch**

Use:

```powershell
& ".\Launch-Crystal-Relay-Debug.bat"
```

Verify the standard stable UI remains unchanged without a marker. In an isolated copy of the Debug output, add `bugfix-build.flag` containing `bugfix1`, relaunch, and verify `v3.1.10`, `BUG FIX 1`, title/build identity `v3.1.10-bugfix1`, and no beta badge.

- [ ] **Step 7: Record the post-stable package gate**

Do not run `Build-Crystal-Relay-BugFix.ps1` against `3.1.10` until normal stable tag `v3.1.10` exists. After `v3.1.10` is stable, the first operational validation must:

1. Use an isolated hotfix branch/worktree from `v3.1.10`.
2. Add a real `v3.1.10 bug fix 1` changelog/release-record section.
3. Set `AGENTS.md` lane/identity to `bugfix` / `v3.1.10-bugfix1`.
4. Run the script without `-Publish`.
5. Inspect the flat ZIP, manifest, updater, marker, exactly one versioned executable, and in-place update behavior.
6. Obtain explicit publication authorization before running `-Publish`.

- [ ] **Step 8: Request final code review**

Invoke `superpowers:requesting-code-review` and give the reviewer this exact scope:

```text
Review the Bug Fix Push implementation against docs/superpowers/specs/2026-07-19-bug-fix-push-channel-design.md. Prioritize behavioral or security defects in: stable > Bug Fix > beta candidate precedence; exact-base and cumulative-sequence checks; no persistent Bug Fix ignore path; old-client isolation through the dedicated asset prefix; package asset/manifest/entry/marker validation; in-place install policy in both the app and dedicated updater; build/publish script path safety and preconditions; GitHub digest verification; and localization key/placeholder integrity. Report Critical and Important findings with file/line references and identify missing tests.
```

Resolve every Critical or Important finding, rerun the affected targeted tests, and request follow-up review before completion.

- [ ] **Step 9: Final commit checkpoint only when explicitly authorized**

If the user explicitly authorizes a final commit, inspect `git status`, `git diff`, and recent log first, stage only the intended files, and create a concise repository-style commit. Otherwise leave all implementation changes uncommitted.
