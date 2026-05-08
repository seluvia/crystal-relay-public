param(
    [string]$PublicRepoPath = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GitHub\crystal-relay-public')
)

$ErrorActionPreference = "Stop"

function Resolve-RequiredPath {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Path not found: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Write-SharedGitIgnore {
    param([string]$Path)

    $gitIgnore = @'
.vs/
.vscode/
.idea/

.appdata/
.dotnet/
.dotnet-home/
.nuget/
.wrangler/
**/.wrangler/

Backups/
Releases/
TestBuilds/
Code Review/
temp-build/

**/bin/
**/obj/

*.user
*.suo
*.cache
*.tmp
*.nupkg

appsettings.Development.json
secrets.json
bridge.runtime.local.json
*.local.json
'@

    Set-Content -LiteralPath $Path -Value $gitIgnore.Trim() -Encoding UTF8
}

function Assert-PublicExportClean {
    param([string]$Path)

    $blockedPaths = @(
        '.appdata',
        '.dotnet',
        '.nuget',
        'Backups',
        'Releases',
        'TestBuilds',
        'Code Review',
        'temp-build',
        'cloudflare',
        'tools'
    )

    foreach ($blockedPath in $blockedPaths) {
        $candidate = Join-Path $Path $blockedPath
        if (Test-Path -LiteralPath $candidate) {
            throw "Public export still contains blocked path: $blockedPath"
        }
    }

    $blockedPatterns = @(
        'About fallback relay values',
        'SupplementalAboutProfilesHeaderValue = "',
        'crl_abt_',
        'E:\!!!Program to work on\Crystal Relay',
        'E:\!!!Program to work on\Proper Crystal Relay',
        'C:\Users\screm\AppData',
        'C:\Users\screm\Documents\GitHub\crystal-relay-private'
    )

    foreach ($pattern in $blockedPatterns) {
        $matches = rg --fixed-strings --line-number --glob '!**/.git/**' --glob '!**/bin/**' --glob '!**/obj/**' -- "$pattern" $Path
        if ($LASTEXITCODE -eq 0) {
            $matchText = $matches -join [Environment]::NewLine
            throw "Public export matched blocked pattern '$pattern':$([Environment]::NewLine)$matchText"
        }

        if ($LASTEXITCODE -gt 1) {
            throw "rg failed while checking blocked pattern '$pattern'."
        }
    }
}

$sourceRoot = Resolve-RequiredPath (Join-Path $PSScriptRoot '..\..')
$publicRoot = Resolve-RequiredPath $PublicRepoPath
$publicGitPath = Join-Path $publicRoot '.git'

if (-not (Test-Path -LiteralPath $publicGitPath)) {
    throw "Public repo path must be a git working copy: $publicRoot"
}

if ($sourceRoot.TrimEnd('\') -ieq $publicRoot.TrimEnd('\')) {
    throw "Source and public repo paths must be different."
}

$excludedDirs = @(
    '.git',
    '.appdata',
    '.dotnet',
    '.dotnet-home',
    '.nuget',
    '.vs',
    'Backups',
    'Releases',
    'TestBuilds',
    'Code Review',
    'temp-build',
    'bin',
    'obj',
    'cloudflare',
    'tools'
)
$excludedFiles = @(
    'AGENTS.md',
    'GITHUB-UPLOAD-NOTES.txt',
    'RELEASE-CHANGE-RECORD.txt',
    'Backup-Crystal-Relay-AppData.ps1',
    'Open-Crystal-Relay-GitHub-Desktop-Workflow.ps1',
    'Prepare-Crystal-Relay-GitHub-Uploads.ps1',
    'Sync-Crystal-Relay-GitHub-Repos.ps1',
    '*.user',
    '*.suo',
    '*.tmp',
    '*.cache',
    '*.nupkg',
    '*.local.json',
    'secrets.json',
    'appsettings.Development.json',
    'bridge.runtime.local.json'
)

& robocopy $sourceRoot $publicRoot /MIR /R:1 /W:1 /XD $excludedDirs /XF $excludedFiles | Out-Null
if ($LASTEXITCODE -gt 7) {
    throw "robocopy failed while exporting public repo with exit code $LASTEXITCODE."
}

Write-SharedGitIgnore -Path (Join-Path $publicRoot '.gitignore')
Assert-PublicExportClean -Path $publicRoot

Write-Host "Public repo export complete: $publicRoot"
