param(
    [string]$Version,
    [string]$PrivateRepoPath = 'C:\Users\screm\Documents\GitHub\crystal-relay-private',
    [string]$PublicRepoPath = 'C:\Users\screm\Documents\GitHub\crystal-relay-public',
    [switch]$SkipOpenDesktop
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

function Set-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    Set-Content -Path $Path -Value $Content -Encoding UTF8
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'VrcTwitchOscBridge\VrcTwitchOscBridge.csproj'
$syncScriptPath = Join-Path $root 'Sync-Crystal-Relay-GitHub-Repos.ps1'
$uploadRoot = Join-Path $root 'Code Review\GitHub Upload'
$templatesDir = Join-Path $uploadRoot 'Commit Templates'
$workflowPath = Join-Path $uploadRoot 'GITHUB-DESKTOP-WORKFLOW.txt'
$githubDesktopExePath = 'C:\Users\screm\AppData\Local\GitHubDesktop\GitHubDesktop.exe'
$openDesktop = -not $SkipOpenDesktop

[xml]$projectXml = Get-Content -Path $projectPath
$currentVersion = Get-VersionText -ProjectXml $projectXml
$targetVersion = if ([string]::IsNullOrWhiteSpace($Version)) { $currentVersion } else { $Version.Trim() }

$syncArguments = @{}
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $syncArguments['Version'] = $Version.Trim()
}

& $syncScriptPath @syncArguments

New-Item -ItemType Directory -Path $templatesDir -Force | Out-Null

$privateSummary = "Sync Crystal Relay updates v$targetVersion"
$privateDescription = @"
Private repo sync prepared from the current working Crystal Relay project.
- Includes the current internal workflow copy
- Includes the current About fallback implementation
"@

$publicSummary = "Sync public Crystal Relay source v$targetVersion"
$publicDescription = @"
Public repo sync prepared from the current working Crystal Relay project.
- Includes the public-safe About cleanup
- Excludes the personal About fallback relay wiring
"@

$privateTemplatePath = Join-Path $templatesDir 'private-commit-template.txt'
$publicTemplatePath = Join-Path $templatesDir 'public-commit-template.txt'

Set-Utf8File -Path $privateTemplatePath -Content (($privateSummary + "`r`n`r`n" + $privateDescription.Trim()))
Set-Utf8File -Path $publicTemplatePath -Content (($publicSummary + "`r`n`r`n" + $publicDescription.Trim()))

$workflow = @"
Crystal Relay GitHub Desktop quick workflow

1. Run:
   powershell -ExecutionPolicy Bypass -File "$($MyInvocation.MyCommand.Path)"

2. GitHub Desktop opens.

3. Commit the private repo first.
   Summary:
   $privateSummary

4. Push the private repo.

5. Switch to the public repo in GitHub Desktop.
   Summary:
   $publicSummary

6. Push the public repo.

Commit template files:
- $privateTemplatePath
- $publicTemplatePath
"@
Set-Utf8File -Path $workflowPath -Content $workflow.Trim()

try {
    Set-Clipboard -Value $privateSummary
}
catch {
    # Clipboard is best-effort only.
}

if ($openDesktop) {
    if (-not (Test-Path $githubDesktopExePath)) {
        throw "GitHub Desktop executable not found at expected path: $githubDesktopExePath"
    }

    Start-Process -FilePath $githubDesktopExePath | Out-Null
}

Write-Host "Version:                 $targetVersion"
Write-Host "Private commit summary:  $privateSummary"
Write-Host "Public commit summary:   $publicSummary"
Write-Host "Private template file:   $privateTemplatePath"
Write-Host "Public template file:    $publicTemplatePath"
Write-Host "Workflow note:           $workflowPath"
if ($openDesktop) {
    Write-Host "GitHub Desktop:          opened"
}
else {
    Write-Host "GitHub Desktop:          skipped"
}
