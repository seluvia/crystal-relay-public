param(
    [ValidateSet("major", "minor", "patch", "mid", "small")]
    [string]$Bump,

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

function Get-BumpedVersion {
    param(
        [string]$CurrentVersion,
        [string]$BumpType
    )

    $parts = $CurrentVersion.Split('.')
    if ($parts.Length -ne 3) {
        throw "Expected a semantic version like 1.2.3, but found '$CurrentVersion'."
    }

    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]

    switch ($BumpType.ToLowerInvariant()) {
        "major" { return Normalize-VersionText -VersionText ("{0}.0.0" -f ($major + 1)) }
        "minor" { return Normalize-VersionText -VersionText ("{0}.{1}.0" -f $major, ($minor + 1)) }
        "mid" { return Normalize-VersionText -VersionText ("{0}.{1}.0" -f $major, ($minor + 1)) }
        "patch" { return Normalize-VersionText -VersionText ("{0}.{1}.{2}" -f $major, $minor, ($patch + 1)) }
        "small" { return Normalize-VersionText -VersionText ("{0}.{1}.{2}" -f $major, $minor, ($patch + 1)) }
        default { throw "Unsupported bump type '$BumpType'." }
    }
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

[xml]$projectXml = Get-Content -Path $projectPath
[xml]$updaterProjectXml = Get-Content -Path $updaterProjectPath
$currentVersion = Get-VersionText -ProjectXml $projectXml

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    if (-not (Test-SemanticVersion -VersionText $Version)) {
        throw "Version must use major.minor.patch format, like 1.2.3."
    }

    $targetVersion = Normalize-VersionText -VersionText $Version
}
elseif (-not [string]::IsNullOrWhiteSpace($Bump)) {
    $targetVersion = Get-BumpedVersion -CurrentVersion $currentVersion -BumpType $Bump
}
else {
    $targetVersion = Normalize-VersionText -VersionText $currentVersion
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

$versionFolderName = "v$targetVersion"
$releaseName = "CrystalRelayTwitchOsc-v$targetVersion-$runtime"
$versionRoot = Join-Path $releaseRoot $versionFolderName
$publishDir = Join-Path $versionRoot $releaseName
$zipPath = Join-Path $versionRoot "$releaseName.zip"

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null
if (Test-Path $publishDir) {
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

$defaultExe = Join-Path $publishDir 'CrystalRelayTwitchOsc.exe'
$versionedExe = Join-Path $publishDir "CrystalRelayTwitchOsc-v$targetVersion.exe"
if (Test-Path $defaultExe) {
    Rename-Item -Path $defaultExe -NewName (Split-Path -Path $versionedExe -Leaf) -Force
}

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
Remove-Item -Path $updaterPublishDir -Recurse -Force

Copy-Item -Path $readmePath -Destination (Join-Path $publishDir 'README.md') -Force
Copy-Item -Path $changelogPath -Destination (Join-Path $publishDir 'CHANGELOG.txt') -Force
if (Test-Path $docsPath) {
    Copy-Item -Path $docsPath -Destination (Join-Path $publishDir 'docs') -Recurse -Force
}

$entryExecutableName = Split-Path -Path $versionedExe -Leaf
$updateManifest = [ordered]@{
    productName = 'Crystal Relay'
    version = $targetVersion
    channel = 'stable'
    runtime = $runtime
    entryExecutableName = $entryExecutableName
}
$updateManifest |
    ConvertTo-Json |
    Set-Content -Path (Join-Path $publishDir 'crystal-relay-update.json') -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Compress-Archive -Path $publishDir -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Version: $targetVersion"
Write-Host "Version Folder: $versionRoot"
Write-Host "Folder:  $publishDir"
Write-Host "Zip:     $zipPath"
