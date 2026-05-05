param(
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 999)]
    [int]$Beta
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

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'VrcTwitchOscBridge\VrcTwitchOscBridge.csproj'
$localizationAuditProject = Join-Path $root 'LocalizationAudit\LocalizationAudit.csproj'
$localizationRoot = Join-Path $root 'VrcTwitchOscBridge\Resources\Localization'
$readmePath = Join-Path $root 'README.md'
$changelogPath = Join-Path $root 'CHANGELOG.txt'
$docsPath = Join-Path $root 'docs'
$releaseRoot = Join-Path $root 'Releases'
$publishConfig = 'Release'
$runtime = 'win-x64'

[xml]$projectXml = Get-Content -Path $projectPath
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

$betaName = "beta$Beta"
$betaLabel = "Beta $Beta"
$versionFolderName = "v$targetVersion"
$releaseName = "CrystalRelayTwitchOsc-v$targetVersion-$betaName-$runtime"
$versionRoot = Join-Path $releaseRoot $versionFolderName
$publishDir = Join-Path $versionRoot $releaseName
$zipPath = Join-Path $versionRoot "$releaseName.zip"
$betaMarkerPath = Join-Path $publishDir 'beta-build.flag'

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
$versionedExe = Join-Path $publishDir "CrystalRelayTwitchOsc-v$targetVersion-$betaName.exe"
if (Test-Path $defaultExe) {
    Rename-Item -Path $defaultExe -NewName (Split-Path -Path $versionedExe -Leaf) -Force
}

Set-Content -Path $betaMarkerPath -Value $betaLabel -Encoding ASCII
Copy-Item -Path $readmePath -Destination (Join-Path $publishDir 'README.md') -Force
Copy-Item -Path $changelogPath -Destination (Join-Path $publishDir 'CHANGELOG.txt') -Force
if (Test-Path $docsPath) {
    Copy-Item -Path $docsPath -Destination (Join-Path $publishDir 'docs') -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Compress-Archive -Path $publishDir -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Version: $targetVersion"
Write-Host "Beta:    $betaLabel"
Write-Host "Version Folder: $versionRoot"
Write-Host "Folder:  $publishDir"
Write-Host "Zip:     $zipPath"
