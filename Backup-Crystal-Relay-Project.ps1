param(
    [string]$Version,
    [switch]$TestBackup
)

$ErrorActionPreference = "Stop"

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Path $ProjectPath
    $versionNode = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($versionNode)) {
        throw "Could not find a <Version> node in the project file."
    }

    return $versionNode.Trim()
}

function Copy-DirectoryFiltered {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    $excludedDirectoryNames = @('bin', 'obj', '.vs', 'temp-build')

    foreach ($item in Get-ChildItem -Path $SourcePath -Force) {
        if ($item.PSIsContainer) {
            $normalizedName = $item.Name.Trim()
            if ($normalizedName -in $excludedDirectoryNames -or $normalizedName.StartsWith('temp-build', [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            Copy-DirectoryFiltered -SourcePath $item.FullName -DestinationPath (Join-Path $DestinationPath $item.Name)
            continue
        }

        Copy-Item -Path $item.FullName -Destination (Join-Path $DestinationPath $item.Name) -Force
    }
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'VrcTwitchOscBridge\VrcTwitchOscBridge.csproj'
$backupRoot = Join-Path $root 'Backups'
$backupLaneRoot = if ($TestBackup) {
    Join-Path $backupRoot 'Test'
}
else {
    $backupRoot
}
$versionText = if ([string]::IsNullOrWhiteSpace($Version)) {
    Get-ProjectVersion -ProjectPath $projectPath
}
else {
    $Version.Trim()
}

if ($versionText -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use major.minor.patch format, like 1.0.1."
}

$versionFolder = Join-Path $backupLaneRoot "v$versionText"
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupName = if ($TestBackup) {
    "CrystalRelayTwitchOsc-v$versionText-test-restore-$timestamp"
}
else {
    "CrystalRelayTwitchOsc-v$versionText-restore-$timestamp"
}
$stagingPath = Join-Path $versionFolder $backupName
$zipPath = Join-Path $versionFolder "$backupName.zip"

New-Item -ItemType Directory -Path $versionFolder -Force | Out-Null
if (Test-Path $stagingPath) {
    Remove-Item -Path $stagingPath -Recurse -Force
}

$rootFiles =
@(
    'Build-Crystal-Relay-Release.ps1',
    'Backup-Crystal-Relay-Project.ps1',
    'Launch-Crystal-Relay.bat',
    'NuGet.Config',
    'README.txt',
    'CHANGELOG.txt',
    'VrcTwitchOscBridge.slnx'
)

$rootDirectories =
@(
    'VrcTwitchOscBridge',
    'oscquery-lib'
)

New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null

foreach ($fileName in $rootFiles) {
    $sourceFile = Join-Path $root $fileName
    if (Test-Path $sourceFile) {
        Copy-Item -Path $sourceFile -Destination (Join-Path $stagingPath $fileName) -Force
    }
}

foreach ($directoryName in $rootDirectories) {
    $sourceDirectory = Join-Path $root $directoryName
    if (Test-Path $sourceDirectory) {
        Copy-DirectoryFiltered -SourcePath $sourceDirectory -DestinationPath (Join-Path $stagingPath $directoryName)
    }
}

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Compress-Archive -Path $stagingPath -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -Path $stagingPath -Recurse -Force

Write-Host "Version: $versionText"
Write-Host "Mode:    $(if ($TestBackup) { 'Test backup' } else { 'Restore backup' })"
Write-Host "Folder:  $versionFolder"
Write-Host "Zip:     $zipPath"
