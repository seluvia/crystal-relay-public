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

$appDataSource = Join-Path $env:LOCALAPPDATA 'CrystalRelay'
if (-not (Test-Path $appDataSource)) {
    throw "Crystal Relay app data folder was not found: $appDataSource"
}

$versionFolder = Join-Path $backupLaneRoot "v$versionText"
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupName = if ($TestBackup) {
    "CrystalRelayTwitchOsc-v$versionText-test-appdata-$timestamp"
}
else {
    "CrystalRelayTwitchOsc-v$versionText-appdata-$timestamp"
}
$stagingPath = Join-Path $versionFolder $backupName
$zipPath = Join-Path $versionFolder "$backupName.zip"
$stagedLocalAppDataPath = Join-Path $stagingPath 'AppData\Local'
$notesPath = Join-Path $stagingPath 'BACKUP-NOTES.txt'

New-Item -ItemType Directory -Path $versionFolder -Force | Out-Null
if (Test-Path $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}

New-Item -ItemType Directory -Path $stagedLocalAppDataPath -Force | Out-Null
Copy-Item -LiteralPath $appDataSource -Destination $stagedLocalAppDataPath -Recurse -Force

$notes = @"
Crystal Relay local app-data backup

Created:
- $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

Version:
- $versionText

Source folder:
- $appDataSource

Included:
- local settings and runtime config
- avatar cache and OSC parameter cache
- save-transfer files
- crash logs and recovery files

Not included:
- Windows Credential Manager secrets
- Twitch OAuth tokens stored outside the app-data folder
- VRChat auth cookie stored outside the app-data folder

If Windows credentials are missing on restore, reconnect Twitch and VRChat inside the app.
"@
Set-Content -Path $notesPath -Value $notes.Trim() -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path $stagingPath -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $stagingPath -Recurse -Force

Write-Host "Version: $versionText"
Write-Host "Mode:    $(if ($TestBackup) { 'Test app-data backup' } else { 'App-data backup' })"
Write-Host "Source:  $appDataSource"
Write-Host "Folder:  $versionFolder"
Write-Host "Zip:     $zipPath"
