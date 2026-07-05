# Crystal Relay File Naming Restructure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simplify Crystal Relay release/beta packages: static folder name `Crystal Relay`, static exe name `Crystal Relay.exe`, version indicated by a `<ver>.txt` dummy file. Updater remains compatible.

**Architecture:** Changes span two build scripts (Release, Beta) and three C# files (app's update services + updater). The ZIP asset name on GitHub changes to `CrystalRelay-v<ver>-win-x64.zip` but the extracted folder is always `Crystal Relay`. The updater resolves packages by manifest location, not folder name pattern.

**Tech Stack:** PowerShell (build scripts), C# / .NET (updater services)

---

### Task 1: Update `ApplicationUpdateService.cs` — ZIP asset URL pattern

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\ApplicationUpdateService.cs:192-193`

- [ ] **Change the ZIP asset name pattern**

Change `GetExpectedAssetName` to use the new prefix:

```csharp
// Line 192-193: From:
private static string GetExpectedAssetName(AppReleaseVersion version) =>
    $"CrystalRelayTwitchOsc-v{version.ToDisplayString()}-{RuntimeName}.zip";

// To:
private static string GetExpectedAssetName(AppReleaseVersion version) =>
    $"CrystalRelay-v{version.ToDisplayString()}-{RuntimeName}.zip";
```

This controls which GitHub release asset the app looks for when checking for updates.

- [ ] **Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 2: Update `ApplicationSelfUpdateService.cs` — package constants and validation

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\ApplicationSelfUpdateService.cs:30`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\ApplicationSelfUpdateService.cs:34`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\ApplicationSelfUpdateService.cs:278-280`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\ApplicationSelfUpdateService.cs:1066-1071`

- [ ] **Change `PackageFolderPrefix` constant**

```csharp
// Line 30: From:
private const string PackageFolderPrefix = "CrystalRelayTwitchOsc-v";
// To:
private const string PackageFolderPrefix = "CrystalRelay-v";
```

- [ ] **Change `ExecutableSearchPattern` constant**

```csharp
// Line 34: From:
private const string ExecutableSearchPattern = "CrystalRelayTwitchOsc-v*.exe";
// To:
private const string ExecutableSearchPattern = "Crystal Relay.exe";
```

- [ ] **Change `ValidateReleaseAsset` expected asset name**

```csharp
// Lines 278-280: From:
var expectedAssetName = update.IsBeta
    ? $"CrystalRelayTwitchOsc-v{update.LatestVersion}-win-x64.zip"
    : $"CrystalRelayTwitchOsc-v{update.LatestVersion}-win-x64.zip";
// To:
var expectedAssetName = update.IsBeta
    ? $"CrystalRelay-v{update.LatestVersion}-win-x64.zip"
    : $"CrystalRelay-v{update.LatestVersion}-win-x64.zip";
```

- [ ] **Update `IsPackageInstallFolderName` to accept `"Crystal Relay"`**

```csharp
// Lines 1066-1071: From:
private static bool IsPackageInstallFolderName(string? folderName) =>
    !string.IsNullOrWhiteSpace(folderName)
    && folderName.StartsWith(PackageFolderPrefix, StringComparison.OrdinalIgnoreCase)
    && folderName.EndsWith($"-{RuntimeName}", StringComparison.OrdinalIgnoreCase)
    && !folderName.Contains(Path.DirectorySeparatorChar)
    && !folderName.Contains(Path.AltDirectorySeparatorChar);

// To:
private static bool IsPackageInstallFolderName(string? folderName) =>
    !string.IsNullOrWhiteSpace(folderName)
    && !folderName.Contains(Path.DirectorySeparatorChar)
    && !folderName.Contains(Path.AltDirectorySeparatorChar)
    && (string.Equals(folderName, "Crystal Relay", StringComparison.OrdinalIgnoreCase)
        || (folderName.StartsWith(PackageFolderPrefix, StringComparison.OrdinalIgnoreCase)
            && folderName.EndsWith($"-{RuntimeName}", StringComparison.OrdinalIgnoreCase)));
```

- [ ] **Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 3: Update `CrystalRelayUpdater\Program.cs` — package constants and validation

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\CrystalRelayUpdater\Program.cs:14`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\CrystalRelayUpdater\Program.cs:17`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\CrystalRelayUpdater\Program.cs:609-614`

- [ ] **Change `PackageFolderPrefix` constant**

```csharp
// Line 14: From:
private const string PackageFolderPrefix = "CrystalRelayTwitchOsc-v";
// To:
private const string PackageFolderPrefix = "CrystalRelay-v";
```

- [ ] **Change `ExecutableSearchPattern` constant**

```csharp
// Line 17: From:
private const string ExecutableSearchPattern = "CrystalRelayTwitchOsc-v*.exe";
// To:
private const string ExecutableSearchPattern = "Crystal Relay.exe";
```

- [ ] **Update `IsPackageInstallFolderName` to accept `"Crystal Relay"`**

```csharp
// Lines 609-614: From:
private static bool IsPackageInstallFolderName(string? folderName) =>
    !string.IsNullOrWhiteSpace(folderName)
    && folderName.StartsWith(PackageFolderPrefix, StringComparison.OrdinalIgnoreCase)
    && folderName.EndsWith($"-{RuntimeName}", StringComparison.OrdinalIgnoreCase)
    && !folderName.Contains(Path.DirectorySeparatorChar)
    && !folderName.Contains(Path.AltDirectorySeparatorChar);

// To:
private static bool IsPackageInstallFolderName(string? folderName) =>
    !string.IsNullOrWhiteSpace(folderName)
    && !folderName.Contains(Path.DirectorySeparatorChar)
    && !folderName.Contains(Path.AltDirectorySeparatorChar)
    && (string.Equals(folderName, "Crystal Relay", StringComparison.OrdinalIgnoreCase)
        || (folderName.StartsWith(PackageFolderPrefix, StringComparison.OrdinalIgnoreCase)
            && folderName.EndsWith($"-{RuntimeName}", StringComparison.OrdinalIgnoreCase)));
```

- [ ] **Build the updater to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\CrystalRelayUpdater\CrystalRelayUpdater.csproj" --no-restore`
Expected: Build succeeds

---

### Task 4: Update `Build-Crystal-Relay-Release.ps1`

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Release.ps1:219-222`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Release.ps1:227`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Release.ps1:267-271`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Release.ps1:300 (add version file)`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Release.ps1:312-322`

- [ ] **Change `$releaseName` and `$zipPath` to use new ZIP asset name**

```powershell
# Lines 218-222: From:
$versionFolderName = "v$targetVersion"
$releaseName = "CrystalRelayTwitchOsc-v$targetVersion-$runtime"
$versionRoot = Join-Path $releaseRoot $versionFolderName
$publishDir = Join-Path $versionRoot $releaseName
$zipPath = Join-Path $versionRoot "$releaseName.zip"

# To:
$versionFolderName = "v$targetVersion"
$releaseName = "CrystalRelay-v$targetVersion-$runtime"
$versionRoot = Join-Path $releaseRoot $versionFolderName
$publishDir = Join-Path $versionRoot 'Crystal Relay'
$zipPath = Join-Path $versionRoot "$releaseName.zip"
```

- [ ] **Change the `Assert-SafeBuildPath` pattern (line 227)**

```powershell
# Line 227: From:
Assert-SafeBuildPath -Path $publishDir -RequiredParent $versionRoot -Pattern "CrystalRelayTwitchOsc-v$targetVersion-win-x64"
# To:
Assert-SafeBuildPath -Path $publishDir -RequiredParent $versionRoot -Pattern "Crystal Relay"
```

- [ ] **Change exe renaming (lines 267-271)**

```powershell
# Lines 267-271: From:
$defaultExe = Join-Path $publishDir 'CrystalRelayTwitchOsc.exe'
$versionedExe = Join-Path $publishDir "CrystalRelayTwitchOsc-v$targetVersion.exe"
if (Test-Path $defaultExe) {
    Rename-Item -Path $defaultExe -NewName (Split-Path -Path $versionedExe -Leaf) -Force
}

# To:
$defaultExe = Join-Path $publishDir 'CrystalRelayTwitchOsc.exe'
$targetExeName = 'Crystal Relay.exe'
if (Test-Path $defaultExe) {
    Rename-Item -Path $defaultExe -NewName $targetExeName -Force
}
```

- [ ] **Add version file creation after exe renaming**

Add this after the exe rename block (after `if (Test-Path $defaultExe) { ... }`):

```powershell
# Create version indicator file (empty, filename = version)
$versionFilePath = Join-Path $publishDir "$targetVersion.txt"
New-Item -Path $versionFilePath -ItemType File -Force | Out-Null
```

- [ ] **Change manifest `entryExecutableName` (lines 312-322)**

```powershell
# Lines 312-322: From:
$entryExecutableName = Split-Path -Path $versionedExe -Leaf
$updateManifest = [ordered]@{
    productName = 'Crystal Relay'
    version = $targetVersion
    channel = 'stable'
    runtime = $runtime
    entryExecutableName = $entryExecutableName
}

# To:
$updateManifest = [ordered]@{
    productName = 'Crystal Relay'
    version = $targetVersion
    channel = 'stable'
    runtime = $runtime
    entryExecutableName = 'Crystal Relay.exe'
}
```

---

### Task 5: Update `Build-Crystal-Relay-Beta.ps1`

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Beta.ps1:188-193`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Beta.ps1:198`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Beta.ps1:238-242`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Beta.ps1:271 (replace beta flag with version file)`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Beta.ps1:284-294`

- [ ] **Change `$releaseName` and `$zipPath` to use new ZIP asset name**

```powershell
# Lines 188-193: From:
$betaName = "beta$Beta"
$betaLabel = "Beta $Beta"
$versionFolderName = "v$targetVersion"
$releaseName = "CrystalRelayTwitchOsc-v$targetVersion-$betaName-$runtime"
$versionRoot = Join-Path $releaseRoot $versionFolderName
$publishDir = Join-Path $versionRoot $releaseName
$zipPath = Join-Path $versionRoot "$releaseName.zip"
$betaMarkerPath = Join-Path $publishDir 'beta-build.flag'

# To:
$betaName = "beta$Beta"
$betaLabel = "Beta $Beta"
$versionFolderName = "v$targetVersion"
$releaseName = "CrystalRelay-v$targetVersion-$betaName-$runtime"
$versionRoot = Join-Path $releaseRoot $versionFolderName
$publishDir = Join-Path $versionRoot 'Crystal Relay'
$zipPath = Join-Path $versionRoot "$releaseName.zip"
```

- [ ] **Change `Assert-SafeBuildPath` pattern (line 198)**

```powershell
# Line 198: From:
Assert-SafeBuildPath -Path $publishDir -RequiredParent $versionRoot -Pattern "CrystalRelayTwitchOsc-v$targetVersion-beta$Beta-win-x64"
# To:
Assert-SafeBuildPath -Path $publishDir -RequiredParent $versionRoot -Pattern "Crystal Relay"
```

- [ ] **Change exe renaming (lines 238-242)**

```powershell
# Lines 238-242: From:
$defaultExe = Join-Path $publishDir 'CrystalRelayTwitchOsc.exe'
$versionedExe = Join-Path $publishDir "CrystalRelayTwitchOsc-v$targetVersion-$betaName.exe"
if (Test-Path $defaultExe) {
    Rename-Item -Path $defaultExe -NewName (Split-Path -Path $versionedExe -Leaf) -Force
}

# To:
$defaultExe = Join-Path $publishDir 'CrystalRelayTwitchOsc.exe'
$targetExeName = 'Crystal Relay.exe'
if (Test-Path $defaultExe) {
    Rename-Item -Path $defaultExe -NewName $targetExeName -Force
}
```

- [ ] **Replace `beta-build.flag` with version file (line 271)**

```powershell
# Line 271: From:
Set-Content -Path $betaMarkerPath -Value $betaLabel -Encoding ASCII
# Remove the $betaMarkerPath variable entirely

# To (add after exe rename):
$versionFileName = "$targetVersion-$betaName.txt"
$versionFilePath = Join-Path $publishDir $versionFileName
New-Item -Path $versionFilePath -ItemType File -Force | Out-Null
```

- [ ] **Change manifest `entryExecutableName` (lines 284-294)**

```powershell
# Lines 284-294: From:
$entryExecutableName = Split-Path -Path $versionedExe -Leaf
$updateManifest = [ordered]@{
    productName = 'Crystal Relay'
    version = "$targetVersion-$betaName"
    channel = 'beta'
    runtime = $runtime
    entryExecutableName = $entryExecutableName
}

# To:
$updateManifest = [ordered]@{
    productName = 'Crystal Relay'
    version = "$targetVersion-$betaName"
    channel = 'beta'
    runtime = $runtime
    entryExecutableName = 'Crystal Relay.exe'
}
```

Also remove the unused `$betaMarkerPath` variable from the new code (it was removed when we changed the publishDir/zipPath block).

- [ ] **Build the project to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Self-Review

**1. Spec coverage:** Cross-checking each spec requirement against tasks:

| Spec requirement | Covered by |
|-----------------|------------|
| Folder name = "Crystal Relay" (static) | Task 4, 5 — `$publishDir = 'Crystal Relay'` |
| .exe name = "Crystal Relay.exe" (static) | Task 4, 5 — `$targetExeName = 'Crystal Relay.exe'` |
| Version file `<ver>.txt` for release | Task 4 — `New-Item "$targetVersion.txt"` |
| Version file `<ver>-beta<N>.txt` for beta | Task 5 — `New-Item "$targetVersion-$betaName.txt"` |
| No `beta-build.flag` | Task 5 — removed |
| ZIP asset name `CrystalRelay-v<ver>-win-x64.zip` | Task 4, 5 — `$releaseName = "CrystalRelay-v$targetVersion-$runtime"` |
| Manifest `entryExecutableName` = `Crystal Relay.exe` | Task 4, 5 — manifest change |
| Updater asset URL pattern | Task 1 — `GetExpectedAssetName` |
| Updater constants (PackageFolderPrefix, ExecutableSearchPattern) | Task 2, 3 |
| Updater folder name validation | Task 2, 3 — `IsPackageInstallFolderName` |
| Test builds unchanged | Not modified |

All spec requirements are covered.

**2. Placeholder scan:** No TBD, TODOs, or placeholder patterns found.

**3. Type consistency:** All property names and method signatures used consistently between tasks. `entryExecutableName` matches across manifest creation, manifest validation, and updater code. `PackageFolderPrefix` is consistent between `ApplicationSelfUpdateService.cs` and `Program.cs`.

**4. Ambiguity check:** Each change has exact before/after code. No vague instructions.
