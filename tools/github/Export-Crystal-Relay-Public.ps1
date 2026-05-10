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
        '.codex',
        '.dotnet',
        '.dotnet-home',
        '.nuget',
        '.vs',
        '.wrangler',
        'Backups',
        'Releases',
        'TestBuilds',
        'Code Review',
        'temp-build',
        'cloudflare',
        'tools',
        'bin',
        'obj'
    )

    $blockedFiles = @(
        'AGENTS.md',
        'Backup-Crystal-Relay-Project.ps1',
        'Backup-Crystal-Relay-AppData.ps1',
        'Open-Crystal-Relay-GitHub-Desktop-Workflow.ps1',
        'Prepare-Crystal-Relay-GitHub-Uploads.ps1',
        'Sync-Crystal-Relay-GitHub-Repos.ps1',
        'GITHUB-UPLOAD-NOTES.txt',
        'RELEASE-CHANGE-RECORD.txt'
    )

    foreach ($blockedPath in $blockedPaths) {
        $candidate = Join-Path $Path $blockedPath
        if (Test-Path -LiteralPath $candidate) {
            throw "Public export still contains blocked path: $blockedPath"
        }
    }

    foreach ($blockedFile in $blockedFiles) {
        $candidate = Join-Path $Path $blockedFile
        if (Test-Path -LiteralPath $candidate) {
            throw "Public export still contains blocked file: $blockedFile"
        }
    }

    $blockedPatterns = @(
        'About fallback relay values',
        'crl_abt_',
        'crystal-relay-private',
        'E:\!!!Program to work on\Crystal Relay',
        'E:\!!!Program to work on\Proper Crystal Relay',
        'C:\Users\screm\AppData',
        'C:\Users\screm\Documents\GitHub\crystal-relay-private',
        'Codex',
        'ChatGPT',
        'OpenAI',
        'AI-generated',
        'AI generated',
        'prompt transcript',
        'system prompt',
        'developer message',
        'AGENTS.md'
    )

    $blockedRegexPatterns = @(
        '(?<![A-Za-z0-9])[A-Za-z]:[\\/][^[:space:]\x22<>|]+',
        'SupplementalAboutProfilesHeaderValue\s*=\s*\x22[^\x22]+',
        '\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{20,}\b',
        '\bgithub_pat_[A-Za-z0-9_]{20,}\b',
        '\bsk-[A-Za-z0-9]{20,}\b',
        '\bxox[baprs]-[A-Za-z0-9-]{20,}\b',
        '\bBearer\s+[A-Za-z0-9._~+/=-]{20,}\b',
        '\bOAuth\s+[A-Za-z0-9._~+/=-]{20,}\b',
        '\boauth:[A-Za-z0-9._~+/=-]{20,}\b',
        '\b(access[_-]?token|refresh[_-]?token|client[_-]?secret|api[_-]?key|password|set-cookie|authcookie)\b\s*[:=]\s*[''\x22][A-Za-z0-9._~+/=-]{12,}[''\x22]'
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

    foreach ($pattern in $blockedRegexPatterns) {
        $matches = rg --pcre2 --line-number --glob '!**/.git/**' --glob '!**/bin/**' --glob '!**/obj/**' -- "$pattern" $Path
        if ($LASTEXITCODE -eq 0) {
            $matchText = $matches -join [Environment]::NewLine
            throw "Public export matched blocked regex '$pattern':$([Environment]::NewLine)$matchText"
        }

        if ($LASTEXITCODE -gt 1) {
            throw "rg failed while checking blocked regex '$pattern'."
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
    '.codex',
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
    'Backup-Crystal-Relay-Project.ps1',
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

$publicWorkflowDirectory = Join-Path $publicRoot '.github\workflows'
New-Item -ItemType Directory -Path $publicWorkflowDirectory -Force | Out-Null
Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot 'templates\public-safety.yml') `
    -Destination (Join-Path $publicWorkflowDirectory 'public-safety.yml') `
    -Force

Assert-PublicExportClean -Path $publicRoot

$preflightScript = Join-Path $PSScriptRoot 'Test-Crystal-Relay-PublicSafety.ps1'
if (Test-Path -LiteralPath $preflightScript) {
    & $preflightScript -PublicRepoPath $publicRoot -SkipBuild
}

Write-Host "Public repo export complete: $publicRoot"
