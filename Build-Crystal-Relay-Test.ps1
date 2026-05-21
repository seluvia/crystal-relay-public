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

$versionRoot = Join-Path $testRoot "v$targetVersion"
$packageDir = Join-Path $versionRoot "CrystalRelayTwitchOsc-v$targetVersion-test"
$appDir = Join-Path $packageDir 'App'
$shortcutPath = Join-Path $packageDir 'Crystal Relay Test.lnk'
$testMarkerPath = Join-Path $appDir 'test-build.flag'

New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null

if (Test-Path $packageDir) {
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
Remove-Item -Path $updaterPublishDir -Recurse -Force

Copy-Item -Path $readmePath -Destination (Join-Path $packageDir 'README.md') -Force
Copy-Item -Path $changelogPath -Destination (Join-Path $packageDir 'CHANGELOG.txt') -Force
if (Test-Path $docsPath) {
    Copy-Item -Path $docsPath -Destination (Join-Path $packageDir 'docs') -Recurse -Force
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
