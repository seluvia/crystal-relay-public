param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

function Get-VersionText {
    param([xml]$ProjectXml)

    $versionNode = $ProjectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($versionNode)) {
        throw "Could not find a <Version> node in the project file."
    }

    return $versionNode.Trim()
}

function Set-VersionText {
    param(
        [xml]$ProjectXml,
        [string]$VersionText
    )

    $propertyGroup = $ProjectXml.Project.PropertyGroup | Select-Object -First 1
    if ($null -eq $propertyGroup) {
        throw "Could not find a PropertyGroup in the project file."
    }

    $assemblyVersion = "$VersionText.0"

    $propertyGroup.Version = $VersionText
    $propertyGroup.AssemblyVersion = $assemblyVersion
    $propertyGroup.FileVersion = $assemblyVersion
    $propertyGroup.InformationalVersion = $VersionText
}

function Test-SemanticVersion {
    param([string]$VersionText)

    return $VersionText -match '^\d+\.\d+\.\d+$'
}

function Normalize-VersionText {
    param([string]$VersionText)

    if (-not (Test-SemanticVersion -VersionText $VersionText)) {
        throw "Version must use major.minor.patch format, like 1.2.3."
    }

    $parts = $VersionText.Split('.')
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]

    while ($patch -ge 10) {
        $patch -= 10
        $minor += 1
    }

    while ($minor -ge 10) {
        $minor -= 10
        $major += 1
    }

    return "{0}.{1}.{2}" -f $major, $minor, $patch
}

function Assert-SafeBuildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$RequiredParent,

        [string]$Pattern
    )

    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullParent = [System.IO.Path]::GetFullPath($RequiredParent).TrimEnd('\', '/')

    if (-not $full.StartsWith($fullParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove '$Path': not under '$RequiredParent'."
    }

    if ($Pattern) {
        $leaf = Split-Path -Leaf $full
        if ($leaf -notlike $Pattern) {
            throw "Refusing to remove '$Path': name '$leaf' does not match pattern '$Pattern'."
        }
    }

    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    if ($full.StartsWith($localAppData, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove '$Path': under LocalAppData."
    }
}

function Test-ChangelogHasSection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Header
    )

    if (-not (Test-Path -LiteralPath $changelogPath)) { return $false }
    $text = Get-Content -LiteralPath $changelogPath -Raw
    $pattern = '^' + [regex]::Escape($Header) + '\s*$'
    return [bool]([regex]::IsMatch($text, $pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline))
}

function Test-ChangelogHasSectionForVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionText
    )

    if (-not (Test-Path -LiteralPath $changelogPath)) { return $false }
    $text = Get-Content -LiteralPath $changelogPath -Raw
    $opts = [System.Text.RegularExpressions.RegexOptions]::Multiline
    $stablePattern = '^v' + [regex]::Escape($VersionText) + '\s*$'
    $betaPattern = '^v' + [regex]::Escape($VersionText) + ' beta \d+\s*$'
    return ([regex]::IsMatch($text, $stablePattern, $opts) -or [regex]::IsMatch($text, $betaPattern, $opts))
}

function Test-RecordBaselineMatches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionText
    )

    $recordPath = Join-Path $root 'RELEASE-CHANGE-RECORD.txt'
    if (-not (Test-Path -LiteralPath $recordPath)) { return $true }
    $text = Get-Content -LiteralPath $recordPath -Raw
    $pattern = 'Current working source version:\s*v?' + [regex]::Escape($VersionText) + '(?:\b|$)'
    return [bool]([regex]::IsMatch($text, $pattern))
}

function Test-WorkingTreeClean {
    $gitDir = Join-Path $root '.git'
    if (-not (Test-Path -LiteralPath $gitDir)) { return $true }
    $status = git -C $root status --porcelain 2>$null
    if ($null -eq $status) { return $true }
    return [string]::IsNullOrWhiteSpace(($status | Out-String))
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'VrcTwitchOscBridge\VrcTwitchOscBridge.csproj'
$updaterProjectPath = Join-Path $root 'CrystalRelayUpdater\CrystalRelayUpdater.csproj'
$localizationAuditProject = Join-Path $root 'LocalizationAudit\LocalizationAudit.csproj'
$localizationRoot = Join-Path $root 'VrcTwitchOscBridge\Resources\Localization'
$readmePath = Join-Path $root 'README.md'
$changelogPath = Join-Path $root 'CHANGELOG.txt'
$docsPath = Join-Path $root 'docs'
$testRoot = Join-Path $root 'TestBuilds'
$runtime = 'win-x64'

[xml]$projectXml = Get-Content -Path $projectPath
[xml]$updaterProjectXml = Get-Content -Path $updaterProjectPath
$currentVersion = Get-VersionText -ProjectXml $projectXml
$targetVersion = if ([string]::IsNullOrWhiteSpace($Version)) {
    Normalize-VersionText -VersionText $currentVersion
}
else {
    Normalize-VersionText -VersionText $Version.Trim()
}

if ($targetVersion -ne $currentVersion) {
    Set-VersionText -ProjectXml $projectXml -VersionText $targetVersion
    $projectXml.Save($projectPath)
}

$updaterCurrentVersion = Get-VersionText -ProjectXml $updaterProjectXml
if ($targetVersion -ne $updaterCurrentVersion) {
    Set-VersionText -ProjectXml $updaterProjectXml -VersionText $targetVersion
    $updaterProjectXml.Save($updaterProjectPath)
}

# Pre-flight: catch repeated mistakes before publishing.
# CHANGELOG gate: a test build needs either the v<version> section or a v<version> beta N section.
if (-not (Test-ChangelogHasSectionForVersion -VersionText $targetVersion)) {
    throw "CHANGELOG.txt is missing section 'v$targetVersion' or 'v$targetVersion beta N'. Add it before packaging."
}

# RELEASE-CHANGE-RECORD baseline drift: warn so the next release pass catches it.
if (-not (Test-RecordBaselineMatches -VersionText $targetVersion)) {
    Write-Warning "RELEASE-CHANGE-RECORD.txt 'Current working source version' does not match $targetVersion. Update it before the next release build."
}

# Working tree cleanliness (opt-out with $env:CR_SKIP_GIT_CHECK = '1').
if ($env:CR_SKIP_GIT_CHECK -ne '1' -and -not (Test-WorkingTreeClean)) {
    throw "Refusing to build with a dirty working tree. Commit, stash, or set CR_SKIP_GIT_CHECK=1."
}

$versionRoot = Join-Path $testRoot "v$targetVersion"
$packageDir = Join-Path $versionRoot "CrystalRelayTwitchOsc-v$targetVersion-test"
$appDir = Join-Path $packageDir 'App'
$shortcutPath = Join-Path $packageDir 'Crystal Relay Test.lnk'
$testMarkerPath = Join-Path $appDir 'test-build.flag'

New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null

if (Test-Path $packageDir) {
    Assert-SafeBuildPath -Path $packageDir -RequiredParent $versionRoot -Pattern "CrystalRelayTwitchOsc-v$targetVersion-test"
    Remove-Item -Path $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $appDir -Force | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.nuget\http-cache'
$env:APPDATA = Join-Path $root '.appdata'
$env:HOME = $root

New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $env:APPDATA 'NuGet') -Force | Out-Null

Push-Location $root
try {
    dotnet run --project $localizationAuditProject --configuration Release -- $localizationRoot
}
finally {
    Pop-Location
}

Push-Location (Join-Path $root 'VrcTwitchOscBridge')
try {
    dotnet publish '.\VrcTwitchOscBridge.csproj' `
        -c Release `
        -r $runtime `
        --self-contained true `
        -o $appDir `
        --configfile (Join-Path $root 'NuGet.Config')
}
finally {
    Pop-Location
}

$updaterPublishDir = Join-Path ([System.IO.Path]::GetTempPath()) ("CrystalRelayUpdater-" + [guid]::NewGuid().ToString("N"))
Push-Location (Join-Path $root 'CrystalRelayUpdater')
try {
    dotnet publish '.\CrystalRelayUpdater.csproj' `
        -c Release `
        -r $runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishTrimmed=true `
        -o $updaterPublishDir `
        --configfile (Join-Path $root 'NuGet.Config')
}
finally {
    Pop-Location
}

Copy-Item -Path (Join-Path $updaterPublishDir 'CrystalRelayUpdater.exe') -Destination (Join-Path $appDir 'CrystalRelayUpdater.exe') -Force
try {
    Assert-SafeBuildPath -Path $updaterPublishDir -RequiredParent ([System.IO.Path]::GetTempPath()) -Pattern "CrystalRelayUpdater-*"
    Remove-Item -Path $updaterPublishDir -Recurse -Force
}
catch {
    # Temp cleanup failure should not abort the build. The temp folder will be cleaned by the OS eventually.
    Write-Warning "Could not clean up updater temp folder: $_"
}

Copy-Item -Path $readmePath -Destination (Join-Path $packageDir 'README.md') -Force
Copy-Item -Path $changelogPath -Destination (Join-Path $packageDir 'CHANGELOG.txt') -Force
if (Test-Path $docsPath) {
    $packagedDocsPath = Join-Path $packageDir 'docs'
    Copy-Item -Path $docsPath -Destination $packagedDocsPath -Recurse -Force
    $internalDocsPath = Join-Path $packagedDocsPath 'superpowers'
    if (Test-Path $internalDocsPath) {
        Assert-SafeBuildPath -Path $internalDocsPath -RequiredParent $packagedDocsPath -Pattern 'superpowers'
        Remove-Item -Path $internalDocsPath -Recurse -Force
    }
}

$updateManifest = [ordered]@{
    productName = 'Crystal Relay'
    version = $targetVersion
    channel = 'test'
    runtime = $runtime
    entryExecutableName = 'CrystalRelayTwitchOsc.exe'
}
$updateManifest |
    ConvertTo-Json |
    Set-Content -Path (Join-Path $appDir 'crystal-relay-update.json') -Encoding UTF8

Set-Content -Path $testMarkerPath -Value 'test-build' -Encoding ASCII

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $appDir 'CrystalRelayTwitchOsc.exe'
$shortcut.WorkingDirectory = $appDir
$shortcut.IconLocation = Join-Path $appDir 'CrystalRelayTwitchOsc.exe'
$shortcut.Description = "Launch Crystal Relay test build"
$shortcut.Save()

Write-Host "Version: $targetVersion"
Write-Host "Test Build Folder: $packageDir"
Write-Host "Shortcut: $shortcutPath"
