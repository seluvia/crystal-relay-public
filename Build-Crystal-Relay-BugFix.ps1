param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 999)]
    [int]$BugFix
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

function Test-SemanticVersion {
    param([string]$VersionText)

    return $VersionText -match '^\d+\.\d+\.\d+$'
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

function Test-TagIsAncestor {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    $gitDir = Join-Path $root '.git'
    if (-not (Test-Path -LiteralPath $gitDir)) { return $true }
    $result = git -C $root merge-base --is-ancestor $Tag HEAD 2>$null
    return $LASTEXITCODE -eq 0
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'VrcTwitchOscBridge\VrcTwitchOscBridge.csproj'
$updaterProjectPath = Join-Path $root 'CrystalRelayUpdater\CrystalRelayUpdater.csproj'
$localizationAuditProject = Join-Path $root 'LocalizationAudit\LocalizationAudit.csproj'
$localizationRoot = Join-Path $root 'VrcTwitchOscBridge\Resources\Localization'
$readmePath = Join-Path $root 'README.md'
$changelogPath = Join-Path $root 'CHANGELOG.txt'
$docsPath = Join-Path $root 'docs'
$releaseRoot = Join-Path $root 'Releases'
$publishConfig = 'Release'
$runtime = 'win-x64'

if (-not (Test-SemanticVersion -VersionText $Version)) {
    throw "Version must use major.minor.patch format, like 1.2.3."
}

$bugFixIdentity = "v$Version-bugfix$BugFix"
$changelogHeader = "v$Version bug fix $BugFix"
$releaseName = "CrystalRelayBugFix-v$Version-bugfix$BugFix-$runtime"
$entryExeName = 'Crystal Relay.exe'
$stableTag = "v$Version"

# === Validate project versions ===
[xml]$projectXml = Get-Content -Path $projectPath
$appVersion = Get-VersionText -ProjectXml $projectXml

if ($appVersion -ne $Version) {
    throw "App project version is '$appVersion', but BugFix target base is '$Version'. The app must already be at the target stable version."
}

[xml]$updaterProjectXml = Get-Content -Path $updaterProjectPath
$updaterVersion = Get-VersionText -ProjectXml $updaterProjectXml

if ($updaterVersion -ne $Version) {
    throw "Updater project version is '$updaterVersion', but BugFix target base is '$Version'. The updater must already be at the target stable version."
}

Write-Host "Project versions match target base: $Version"

# === Pre-flight gates ===

# CHANGELOG gate: needs exact 'v<version> bug fix <N>' section
if (-not (Test-ChangelogHasSection -Header $changelogHeader)) {
    throw "CHANGELOG.txt is missing section '$changelogHeader'. Add it before building a bug fix push."
}

# RELEASE-CHANGE-RECORD baseline drift: throw if baseline doesn't match target base
if (-not (Test-RecordBaselineMatches -VersionText $Version)) {
    throw "RELEASE-CHANGE-RECORD.txt 'Current working source version' does not match $Version. Update it before building a bug fix push."
}

# Working tree cleanliness (opt-out with $env:CR_SKIP_GIT_CHECK = '1')
if ($env:CR_SKIP_GIT_CHECK -ne '1' -and -not (Test-WorkingTreeClean)) {
    throw "Refusing to build with a dirty working tree. Commit, stash, or set CR_SKIP_GIT_CHECK=1."
}

# Git ancestry gate: stable tag must be an ancestor of current commit
if (-not (Test-TagIsAncestor -Tag $stableTag)) {
    throw "Stable tag '$stableTag' is not an ancestor of the current commit. BugFix builds must branch from the tagged stable release."
}

# === Output paths ===
$versionFolderName = "v$Version"
$versionRoot = Join-Path $releaseRoot $versionFolderName
$publishDir = Join-Path $versionRoot 'Crystal Relay'
$zipPath = Join-Path $versionRoot "$releaseName.zip"

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null
if (Test-Path $publishDir) {
    Assert-SafeBuildPath -Path $publishDir -RequiredParent $versionRoot -Pattern "Crystal Relay"
    Remove-Item -Path $publishDir -Recurse -Force
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

# === Localization audit ===
Push-Location $root
try {
    dotnet run --project $localizationAuditProject --configuration Release -- $localizationRoot
}
finally {
    Pop-Location
}

# === Publish main app ===
Push-Location (Join-Path $root 'VrcTwitchOscBridge')
try {
    dotnet publish '.\VrcTwitchOscBridge.csproj' `
        -c $publishConfig `
        -r $runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishTrimmed=false `
        -o $publishDir `
        --configfile (Join-Path $root 'NuGet.Config')
}
finally {
    Pop-Location
}

# === Rename main executable to static name ===
$defaultExe = Join-Path $publishDir 'CrystalRelayTwitchOsc.exe'
if (Test-Path $defaultExe) {
    Rename-Item -Path $defaultExe -NewName $entryExeName -Force
}

# === Publish updater ===
$updaterPublishDir = Join-Path ([System.IO.Path]::GetTempPath()) ("CrystalRelayUpdater-" + [guid]::NewGuid().ToString("N"))
Push-Location (Join-Path $root 'CrystalRelayUpdater')
try {
    dotnet publish '.\CrystalRelayUpdater.csproj' `
        -c $publishConfig `
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

Copy-Item -Path (Join-Path $updaterPublishDir 'CrystalRelayUpdater.exe') -Destination (Join-Path $publishDir 'CrystalRelayUpdater.exe') -Force
try {
    Assert-SafeBuildPath -Path $updaterPublishDir -RequiredParent ([System.IO.Path]::GetTempPath()) -Pattern "CrystalRelayUpdater-*"
    Remove-Item -Path $updaterPublishDir -Recurse -Force
}
catch {
    Write-Warning "Could not clean up updater temp folder: $_"
}

# === Copy docs ===
Copy-Item -Path $readmePath -Destination (Join-Path $publishDir 'README.md') -Force
Copy-Item -Path $changelogPath -Destination (Join-Path $publishDir 'CHANGELOG.txt') -Force
if (Test-Path $docsPath) {
    $packagedDocsPath = Join-Path $publishDir 'docs'
    Copy-Item -Path $docsPath -Destination $packagedDocsPath -Recurse -Force
    $internalDocsPath = Join-Path $packagedDocsPath 'superpowers'
    if (Test-Path $internalDocsPath) {
        Assert-SafeBuildPath -Path $internalDocsPath -RequiredParent $packagedDocsPath -Pattern 'superpowers'
        Remove-Item -Path $internalDocsPath -Recurse -Force
    }
}

# === Create bugfix-build.flag ===
$flagContent = "bugfix$BugFix"
Set-Content -Path (Join-Path $publishDir 'bugfix-build.flag') -Value $flagContent -Encoding ASCII -NoNewline

# === Create update manifest ===
$updateManifest = [ordered]@{
    productName = 'Crystal Relay'
    version = "$Version-bugfix$BugFix"
    channel = 'bugfix'
    runtime = $runtime
    entryExecutableName = $entryExeName
}
$updateManifest |
    ConvertTo-Json |
    Set-Content -Path (Join-Path $publishDir 'crystal-relay-update.json') -Encoding UTF8

# === Post-build validation ===
$errors = @()

$manifestPath = Join-Path $publishDir 'crystal-relay-update.json'
if (-not (Test-Path $manifestPath)) {
    $errors += "Manifest file not found at $manifestPath"
}
else {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.productName -ne 'Crystal Relay') { $errors += "Manifest productName mismatch: '$($manifest.productName)'" }
    if ($manifest.channel -ne 'bugfix') { $errors += "Manifest channel mismatch: '$($manifest.channel)'" }
    if ($manifest.version -ne "$Version-bugfix$BugFix") { $errors += "Manifest version mismatch: '$($manifest.version)'" }
    if ($manifest.runtime -ne $runtime) { $errors += "Manifest runtime mismatch: '$($manifest.runtime)'" }
    if ($manifest.entryExecutableName -ne $entryExeName) { $errors += "Manifest entryExecutableName mismatch: '$($manifest.entryExecutableName)'" }
}

$flagPath = Join-Path $publishDir 'bugfix-build.flag'
if (-not (Test-Path $flagPath)) {
    $errors += "bugfix-build.flag not found at $flagPath"
}
else {
    $actualFlag = Get-Content $flagPath -Raw
    if ($actualFlag -ne $flagContent) {
        $errors += "bugfix-build.flag content mismatch: expected '$flagContent', got '$actualFlag'"
    }
}

$entryExePath = Join-Path $publishDir $entryExeName
if (-not (Test-Path $entryExePath)) {
    $errors += "Required entry executable not found: $entryExeName"
}

$versionedAppExecutables = @(Get-ChildItem -Path $publishDir -Filter 'CrystalRelayTwitchOsc-v*.exe' -File -Recurse -ErrorAction SilentlyContinue)
if ($versionedAppExecutables.Count -gt 0) {
    $errors += "Versioned Crystal Relay app executable(s) must not be packaged: $($versionedAppExecutables.Name -join ', ')"
}

$updaterExePath = Join-Path $publishDir 'CrystalRelayUpdater.exe'
if (-not (Test-Path $updaterExePath)) {
    $errors += "Updater executable not found at $updaterExePath"
}

if ($errors.Count -gt 0) {
    Write-Host "Package validation FAILED:" -ForegroundColor Red
    foreach ($err in $errors) { Write-Host "  - $err" -ForegroundColor Red }
    throw "BugFix package validation failed. See errors above."
}

# === ZIP the validated package ===
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Compress-Archive -Path $publishDir -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "BugFix push package built successfully:" -ForegroundColor Green
Write-Host "  Identity: $bugFixIdentity"
Write-Host "  Version Folder: $versionRoot"
Write-Host "  Folder:  $publishDir"
Write-Host "  Zip:     $zipPath"
