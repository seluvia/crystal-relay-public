param(
    [string]$Version,
    [switch]$BuildOnly
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
$versionText = if ([string]::IsNullOrWhiteSpace($Version)) {
    Get-ProjectVersion -ProjectPath $projectPath
}
else {
    $Version.Trim()
}

if ([string]::IsNullOrWhiteSpace($versionText)) {
    $versionText = '0.0.0'
}

$runRoot = Join-Path $root 'RunCache'
$versionRoot = Join-Path $runRoot "v$versionText"
$sessionName = Get-Date -Format 'yyyyMMdd-HHmmss'
$sessionRoot = Join-Path $versionRoot $sessionName
$publishRoot = Join-Path $sessionRoot 'publish'
$exePath = Join-Path $publishRoot 'CrystalRelayTwitchOsc.exe'
$dllPath = Join-Path $publishRoot 'CrystalRelayTwitchOsc.dll'

New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.nuget\http-cache'
$env:APPDATA = Join-Path $root '.appdata'
$env:HOME = $root

New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME -Force | Out-Null
New-Item -ItemType Directory -Path $env:NUGET_PACKAGES -Force | Out-Null
New-Item -ItemType Directory -Path $env:NUGET_HTTP_CACHE_PATH -Force | Out-Null
New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $env:APPDATA 'NuGet') -Force | Out-Null

Push-Location (Join-Path $root 'VrcTwitchOscBridge')
try {
    $buildArguments = @(
        'publish'
        '.\VrcTwitchOscBridge.csproj'
        '-c'
        'Release'
        '-r'
        'win-x64'
        '--self-contained'
        'false'
        '--configfile'
        (Join-Path $root 'NuGet.Config')
        '-o'
        $publishRoot
    )

    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Crystal Relay source publish failed."
    }
}
finally {
    Pop-Location
}

if ($BuildOnly) {
    Write-Host "Version: $versionText"
    Write-Host "Session: $sessionRoot"
    return
}

if (Test-Path $exePath) {
    Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Path $exePath -Parent)
}
elseif (Test-Path $dllPath) {
    Start-Process -FilePath 'dotnet' -ArgumentList @($dllPath) -WorkingDirectory (Split-Path -Path $dllPath -Parent)
}
else {
    throw "The source build finished, but Crystal Relay could not find the app output."
}

$oldSessions = Get-ChildItem -Path $versionRoot -Directory -Force |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip 4

foreach ($oldSession in $oldSessions) {
    Remove-Item -Path $oldSession.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Version: $versionText"
Write-Host "Session: $sessionRoot"
