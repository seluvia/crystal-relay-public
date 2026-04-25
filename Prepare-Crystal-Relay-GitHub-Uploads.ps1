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

function Reset-Directory {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Test-ExcludedFileName {
    param([string]$FileName)

    $excludedPatterns = @('*.user', '*.suo', '*.tmp', '*.cache', '*.nupkg')
    foreach ($pattern in $excludedPatterns) {
        if ($FileName -like $pattern) {
            return $true
        }
    }

    return $false
}

function Copy-DirectoryFiltered {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null

    $excludedDirectoryNames = @(
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
        'bin',
        'obj'
    )

    foreach ($item in Get-ChildItem -LiteralPath $SourcePath -Force) {
        $normalizedName = $item.Name.Trim()

        if ($item.PSIsContainer) {
            if ($excludedDirectoryNames -contains $normalizedName -or
                $normalizedName.StartsWith('temp-build', [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            Copy-DirectoryFiltered -SourcePath $item.FullName -DestinationPath (Join-Path $DestinationPath $item.Name)
            continue
        }

        if (Test-ExcludedFileName -FileName $item.Name) {
            continue
        }

        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $DestinationPath $item.Name) -Force
    }
}

function Replace-RequiredText {
    param(
        [string]$Content,
        [string]$OldValue,
        [string]$NewValue,
        [string]$Description
    )

    $updatedContent = $Content.Replace($OldValue, $NewValue)
    if ($updatedContent -eq $Content) {
        throw "Failed to update public upload copy: $Description"
    }

    return $updatedContent
}

function Replace-RequiredBlock {
    param(
        [string]$Content,
        [string]$StartMarker,
        [string]$EndMarker,
        [string]$Replacement,
        [string]$Description
    )

    $startIndex = $Content.IndexOf($StartMarker, [System.StringComparison]::Ordinal)
    if ($startIndex -lt 0) {
        throw "Failed to find start marker for public upload copy: $Description"
    }

    $endIndex = $Content.IndexOf($EndMarker, $startIndex, [System.StringComparison]::Ordinal)
    if ($endIndex -lt 0) {
        throw "Failed to find end marker for public upload copy: $Description"
    }

    return $Content.Substring(0, $startIndex) + $Replacement + $Content.Substring($endIndex)
}

function Set-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    Set-Content -Path $Path -Value $Content -Encoding UTF8
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

Backups/
Releases/
TestBuilds/
Code Review/

**/bin/
**/obj/

*.user
*.suo
*.cache
*.tmp

appsettings.Development.json
secrets.json
'@

    Set-Utf8File -Path $Path -Content $gitIgnore.Trim()
}

function Apply-PublicUploadCleanup {
    param([string]$PublicRoot)

    $twitchApiClientPath = Join-Path $PublicRoot 'VrcTwitchOscBridge\Services\TwitchApiClient.cs'
    $mainWindowViewModelPath = Join-Path $PublicRoot 'VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs'

    $twitchApiClient = Get-Content -Path $twitchApiClientPath -Raw
    $twitchApiClient = Replace-RequiredText `
        -Content $twitchApiClient `
        -OldValue '/// chat sends, and the protected About-page fallback relay.' `
        -NewValue '/// chat sends, and public About-page profile lookups.' `
        -Description 'TwitchApiClient summary comment'
    $twitchApiClient = Replace-RequiredBlock `
        -Content $twitchApiClient `
        -StartMarker '    // The About fallback relay values are split into segments so the desktop app does not' `
        -EndMarker '    private static readonly Regex TwitterImageMetaRegex = new(' `
        -Replacement '' `
        -Description 'About relay field block'
    $twitchApiClient = Replace-RequiredBlock `
        -Content $twitchApiClient `
        -StartMarker '    // Supplemental About data is the no-login fallback path. The Worker returns live state,' `
        -EndMarker '    public void Dispose() => httpClient.Dispose();' `
        -Replacement '' `
        -Description 'Supplemental About relay method'
    $twitchApiClient = Replace-RequiredBlock `
        -Content $twitchApiClient `
        -StartMarker '    // Builds the About relay URL. The Worker now serves the app''s fixed About dataset only.' `
        -EndMarker '    public sealed class DeviceCodeResponse' `
        -Replacement '' `
        -Description 'About relay URL builder'
    $twitchApiClient = Replace-RequiredBlock `
        -Content $twitchApiClient `
        -StartMarker '    public sealed class PublicLiveStatusResponse' `
        -EndMarker '    public sealed class CustomRewardListResponse' `
        -Replacement '' `
        -Description 'Supplemental About relay DTOs'
    Set-Utf8File -Path $twitchApiClientPath -Content $twitchApiClient.TrimEnd()

    $mainWindowViewModel = Get-Content -Path $mainWindowViewModelPath -Raw
    $mainWindowViewModel = Replace-RequiredText `
        -Content $mainWindowViewModel `
        -OldValue '// If the app does not have a usable token, it falls back to the supplemental relay path.' `
        -NewValue '// If the app does not have a usable token, it falls back to public image-only lookups.' `
        -Description 'About refresh comment'
    $mainWindowViewModel = Replace-RequiredBlock `
        -Content $mainWindowViewModel `
        -StartMarker '    // No-login About refresh path. This keeps creator/playtester cards useful on' `
        -EndMarker '    private async Task RefreshAboutProfileImagesWithoutAuthAsync(IReadOnlyList<AboutTwitchProfile> profiles)' `
        -Replacement @'
    // No-login About refresh path. This keeps creator/playtester cards useful on
    // fresh installs by clearing live state before trying public image-only fallbacks.
    private async Task RefreshAboutProfilesWithoutAuthAsync(IReadOnlyList<AboutTwitchProfile> profiles)
    {
        RunOnUi(() => ApplyAboutProfileLiveStates(
            profiles,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)));
        await RefreshAboutProfileImagesWithoutAuthAsync(profiles);

        aboutProfilesLastRefreshedAt = DateTimeOffset.UtcNow;
    }

'@ `
        -Description 'No-auth About refresh method'
    $mainWindowViewModel = Replace-RequiredBlock `
        -Content $mainWindowViewModel `
        -StartMarker '    private static void ApplySupplementalAboutProfiles(' `
        -EndMarker '    private bool HasAboutProfileLookupAccess()' `
        -Replacement '' `
        -Description 'Supplemental About profile apply helper'
    Set-Utf8File -Path $mainWindowViewModelPath -Content $mainWindowViewModel.TrimEnd()
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'VrcTwitchOscBridge\VrcTwitchOscBridge.csproj'
$uploadRoot = Join-Path $root 'Code Review\GitHub Upload'
$privateSyncDir = Join-Path $uploadRoot 'Private Repo Sync'
$publicSyncDir = Join-Path $uploadRoot 'Public Repo Sync'
$packagesDir = Join-Path $uploadRoot 'Packages'
$workflowPath = Join-Path $uploadRoot 'GITHUB-DESKTOP-WORKFLOW.txt'

[xml]$projectXml = Get-Content -Path $projectPath
$currentVersion = Get-VersionText -ProjectXml $projectXml
$targetVersion = if ([string]::IsNullOrWhiteSpace($Version)) { $currentVersion } else { $Version.Trim() }

$publicZipPath = Join-Path $packagesDir "CrystalRelay-GitHub-Public-v$targetVersion-source.zip"

New-Item -ItemType Directory -Path $uploadRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packagesDir -Force | Out-Null

Reset-Directory -Path $privateSyncDir
Reset-Directory -Path $publicSyncDir

Copy-DirectoryFiltered -SourcePath $root -DestinationPath $privateSyncDir
Copy-DirectoryFiltered -SourcePath $root -DestinationPath $publicSyncDir

Write-SharedGitIgnore -Path (Join-Path $privateSyncDir '.gitignore')
Write-SharedGitIgnore -Path (Join-Path $publicSyncDir '.gitignore')

$privateNotes = @"
Crystal Relay private GitHub sync copy

Version:
- v$targetVersion

Purpose:
- Copy this folder's contents into your local private GitHub Desktop repo folder.
- Commit and push from GitHub Desktop after review.

Notes:
- This copy keeps the current internal About fallback implementation intact.
- Build outputs, backups, test packages, and local cache folders are excluded.
"@
Set-Utf8File -Path (Join-Path $privateSyncDir 'GITHUB-UPLOAD-NOTES.txt') -Content $privateNotes.Trim()

Apply-PublicUploadCleanup -PublicRoot $publicSyncDir

$publicNotes = @"
Crystal Relay public GitHub sync copy

Version:
- v$targetVersion

Purpose:
- Copy this folder's contents into your local public GitHub Desktop repo folder.
- Commit and push from GitHub Desktop after review.

Public-safe change:
- The no-auth About fallback relay wiring is removed from this copy.
- Authenticated Twitch About lookups still work normally.
- No-auth About refresh now uses public profile image lookups only.

Notes:
- Build outputs, backups, test packages, and local cache folders are excluded.
- This copy is prepared for the public repo workflow.
"@
Set-Utf8File -Path (Join-Path $publicSyncDir 'GITHUB-UPLOAD-NOTES.txt') -Content $publicNotes.Trim()

$workflow = @"
Crystal Relay GitHub Desktop workflow

Private repo update:
1. Run Prepare-Crystal-Relay-GitHub-Uploads.ps1
2. Copy everything from:
   $privateSyncDir
3. Paste into your local private GitHub repo folder.
4. Review changes in GitHub Desktop.
5. Commit and push.

Public repo update:
1. Run Prepare-Crystal-Relay-GitHub-Uploads.ps1
2. Copy everything from:
   $publicSyncDir
3. Paste into your local public GitHub repo folder.
4. Review changes in GitHub Desktop.
5. Commit and push.

Public source package:
- Zip path:
  $publicZipPath
"@
Set-Utf8File -Path $workflowPath -Content $workflow.Trim()

if (Test-Path $publicZipPath) {
    Remove-Item -LiteralPath $publicZipPath -Force
}

Compress-Archive -Path $publicSyncDir -DestinationPath $publicZipPath -CompressionLevel Optimal

Write-Host "Version:           $targetVersion"
Write-Host "Private Repo Sync: $privateSyncDir"
Write-Host "Public Repo Sync:  $publicSyncDir"
Write-Host "Public Package:    $publicZipPath"
