param(
    [string]$Version,
    [string]$PrivateRepoPath = 'C:\Users\screm\Documents\GitHub\crystal-relay-private',
    [string]$PublicRepoPath = 'C:\Users\screm\Documents\GitHub\crystal-relay-public'
)

$ErrorActionPreference = "Stop"

function Sync-DirectoryContents {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    if (-not (Test-Path $DestinationPath)) {
        throw "Repository folder not found: $DestinationPath"
    }

    & robocopy $SourcePath $DestinationPath /MIR /R:1 /W:1 /XD '.git' | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed while syncing '$SourcePath' to '$DestinationPath' with exit code $LASTEXITCODE."
    }
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$prepareScriptPath = Join-Path $root 'Prepare-Crystal-Relay-GitHub-Uploads.ps1'
$uploadRoot = Join-Path $root 'Code Review\GitHub Upload'
$privateSyncDir = Join-Path $uploadRoot 'Private Repo Sync'
$publicSyncDir = Join-Path $uploadRoot 'Public Repo Sync'

$prepareArguments = @{}
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $prepareArguments['Version'] = $Version.Trim()
}

& $prepareScriptPath @prepareArguments

Sync-DirectoryContents -SourcePath $privateSyncDir -DestinationPath $PrivateRepoPath
Sync-DirectoryContents -SourcePath $publicSyncDir -DestinationPath $PublicRepoPath

Write-Host "Private repo synced: $PrivateRepoPath"
Write-Host "Public repo synced:  $PublicRepoPath"
