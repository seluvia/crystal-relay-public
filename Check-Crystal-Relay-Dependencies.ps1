$ErrorActionPreference = 'Stop'

Write-Host 'Crystal Relay dependency vulnerability scan'
Write-Host "Root: $PSScriptRoot"
Write-Host ''

$excludedPrefixes = @(
    ".git$([System.IO.Path]::DirectorySeparatorChar)",
    "Backups$([System.IO.Path]::DirectorySeparatorChar)",
    "Code Review$([System.IO.Path]::DirectorySeparatorChar)",
    "Releases$([System.IO.Path]::DirectorySeparatorChar)",
    "TestBuilds$([System.IO.Path]::DirectorySeparatorChar)",
    "temp-build$([System.IO.Path]::DirectorySeparatorChar)"
)

$rootPrefix = $PSScriptRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

$projectFiles = Get-ChildItem -Path $PSScriptRoot -Filter '*.csproj' -Recurse -File |
    Where-Object {
        $relativePath = $_.FullName
        if ($relativePath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relativePath = $relativePath.Substring($rootPrefix.Length)
        }

        foreach ($prefix in $excludedPrefixes) {
            if ($relativePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $false
            }
        }

        return $true
    } |
    Sort-Object FullName

if ($projectFiles.Count -eq 0) {
    Write-Error 'No project files were found to scan.'
    exit 1
}

$exitCode = 0
foreach ($projectFile in $projectFiles) {
    $displayPath = $projectFile.FullName
    if ($displayPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        $displayPath = $displayPath.Substring($rootPrefix.Length)
    }

    Write-Host "Scanning $displayPath"
    & dotnet list $projectFile.FullName package --vulnerable --include-transitive
    if ($LASTEXITCODE -ne 0) {
        $exitCode = $LASTEXITCODE
    }

    Write-Host ''
}

if ($exitCode -eq 0) {
    Write-Host 'Dependency scan completed.'
}
else {
    Write-Host "Dependency scan failed or reported an issue. Exit code: $exitCode"
}

exit $exitCode
