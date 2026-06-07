param(
    [string]$PublicRepoPath = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GitHub\crystal-relay-public'),
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Resolve-RequiredPath {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Path not found: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Invoke-CheckedCommand {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @ArgumentList
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Get-PublicCandidateFiles {
    param([string]$Path)

    $files = & git -C $Path ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed for public repo."
    }

    return @(
        $files |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Where-Object { Test-Path -LiteralPath (Join-Path $Path $_) }
    )
}

function Assert-NoBlockedPaths {
    param(
        [string[]]$Files,
        [string[]]$BlockedDirectories,
        [string[]]$BlockedFiles
    )

    $failures = New-Object System.Collections.Generic.List[string]
    $directorySet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $fileSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($directory in $BlockedDirectories) {
        [void]$directorySet.Add($directory)
    }

    foreach ($file in $BlockedFiles) {
        [void]$fileSet.Add($file)
    }

    foreach ($file in $Files) {
        $parts = $file -split '[\\/]'
        foreach ($part in $parts) {
            if ($directorySet.Contains($part)) {
                $failures.Add("$file contains blocked public directory '$part'.")
            }
        }

        $leaf = Split-Path -Leaf $file
        if ($fileSet.Contains($leaf)) {
            $failures.Add("$file is blocked from the public repo.")
        }
    }

    if ($failures.Count -gt 0) {
        throw "Blocked public paths found:$([Environment]::NewLine)$($failures -join [Environment]::NewLine)"
    }
}

function Assert-NoContentMatches {
    param(
        [string]$Path,
        [string[]]$Files,
        [string[]]$Patterns,
        [switch]$Regex
    )

    $binaryExtensions = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@(
            '.avi',
            '.dll',
            '.ico',
            '.jpeg',
            '.jpg',
            '.m4a',
            '.mov',
            '.mp3',
            '.mp4',
            '.nupkg',
            '.png',
            '.wav',
            '.webm',
            '.zip'
        ),
        [StringComparer]::OrdinalIgnoreCase
    )

    $failures = New-Object System.Collections.Generic.List[string]

    foreach ($file in $Files) {
        $fullPath = Join-Path $Path $file
        $extension = [System.IO.Path]::GetExtension($fullPath)
        if ($binaryExtensions.Contains($extension)) {
            continue
        }

        $bytes = [System.IO.File]::ReadAllBytes($fullPath)
        if ([Array]::IndexOf($bytes, [byte]0) -ge 0) {
            continue
        }

        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        foreach ($pattern in $Patterns) {
            $matched = if ($Regex) {
                [regex]::IsMatch($text, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            }
            else {
                $text.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            }

            if ($matched) {
                $failures.Add("$file matched blocked public content pattern '$pattern'.")
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Blocked public content found:$([Environment]::NewLine)$($failures -join [Environment]::NewLine)"
    }
}

$publicRoot = Resolve-RequiredPath $PublicRepoPath

if (-not (Test-Path -LiteralPath (Join-Path $publicRoot '.git'))) {
    throw "Public repo path must be a git working copy: $publicRoot"
}

$candidateFiles = Get-PublicCandidateFiles -Path $publicRoot

Assert-NoBlockedPaths `
    -Files $candidateFiles `
    -BlockedDirectories @(
        '.appdata',
        '.codex',
        '.dotnet',
        '.dotnet-home',
        '.nuget',
        '.opencode',
        '.vs',
        '.wrangler',
        'Backups',
        'Releases',
        'TestBuilds',
        'Code Review',
        'temp-build',
        'superpowers',
        'tools',
        'private',
        'crystal-relay-live-list',
        'cloudflare',
        'bin',
        'obj'
    ) `
    -BlockedFiles @(
        'AGENTS.md',
        'Backup-Crystal-Relay-Project.ps1',
        'Backup-Crystal-Relay-AppData.ps1',
        '!open-opencode.bat',
        '!start-opencode Server.bat',
        'Open-Crystal-Relay-GitHub-Desktop-Workflow.ps1',
        'Prepare-Crystal-Relay-GitHub-Uploads.ps1',
        'Sync-Crystal-Relay-GitHub-Repos.ps1',
        'GITHUB-UPLOAD-NOTES.txt',
        'RELEASE-CHANGE-RECORD.txt'
    )

Assert-NoContentMatches `
    -Path $publicRoot `
    -Files $candidateFiles `
    -Patterns @(
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
        'crystal-relay-live-list',
        'AI-generated',
        'AI generated',
        'prompt transcript',
        'system prompt',
        'developer message',
        'AGENTS.md'
    )

Assert-NoContentMatches `
    -Path $publicRoot `
    -Files $candidateFiles `
    -Regex `
    -Patterns @(
        '(?<![A-Za-z0-9])[A-Za-z]:[\\/][^\s"<>|]+',
        'SupplementalAboutProfilesHeaderValue\s*=\s*"[^"]+',
        '\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{20,}\b',
        '\bgithub_pat_[A-Za-z0-9_]{20,}\b',
        '\bsk-[A-Za-z0-9]{20,}\b',
        '\bxox[baprs]-[A-Za-z0-9-]{20,}\b',
        '\bBearer\s+[A-Za-z0-9._~+/=-]{20,}\b',
        '\bOAuth\s+[A-Za-z0-9._~+/=-]{20,}\b',
        '\boauth:[A-Za-z0-9._~+/=-]{20,}\b',
        '\b(access[_-]?token|refresh[_-]?token|client[_-]?secret|api[_-]?key|password|set-cookie|authcookie)\b\s*[:=]\s*[''"][A-Za-z0-9._~+/=-]{12,}[''"]'
    )

Invoke-CheckedCommand -FilePath 'git' -ArgumentList @('diff', '--check') -WorkingDirectory $publicRoot

if (-not $SkipBuild) {
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('restore', '.\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj') -WorkingDirectory $publicRoot
    Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('build', '.\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj', '--no-restore', '-c', 'Release') -WorkingDirectory $publicRoot
}

Write-Host "Public safety preflight passed: $publicRoot"
