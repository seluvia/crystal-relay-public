@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "APP_NAME=Crystal Relay - Twitch to OSC"
set "BUILD_SCRIPT=%ROOT%Build-Crystal-Relay-Release.ps1"
set "PROJECT_PATH=%ROOT%VrcTwitchOscBridge\VrcTwitchOscBridge.csproj"
set "APP_VERSION="
set "TARGET_RELEASE_EXE="
set "LATEST_RELEASE_EXE="

set "DOTNET_CLI_HOME=%ROOT%.dotnet-home"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "NUGET_PACKAGES=%ROOT%.nuget\packages"
set "NUGET_HTTP_CACHE_PATH=%ROOT%.nuget\http-cache"
set "APPDATA=%ROOT%.appdata"
set "HOME=%ROOT%"

if not exist "%DOTNET_CLI_HOME%" mkdir "%DOTNET_CLI_HOME%"
if not exist "%NUGET_PACKAGES%" mkdir "%NUGET_PACKAGES%"
if not exist "%NUGET_HTTP_CACHE_PATH%" mkdir "%NUGET_HTTP_CACHE_PATH%"
if not exist "%APPDATA%" mkdir "%APPDATA%"
if not exist "%APPDATA%\NuGet" mkdir "%APPDATA%\NuGet"

call :resolve_version
call :resolve_target_release
call :resolve_latest_release

if defined TARGET_RELEASE_EXE if exist "%TARGET_RELEASE_EXE%" goto launch_target

where dotnet >nul 2>nul
if errorlevel 1 goto launch_existing

if not exist "%BUILD_SCRIPT%" (
    echo The release build script could not be found.
    goto launch_existing
)

if defined APP_VERSION (
    echo Building Crystal Relay - Twitch to OSC v%APP_VERSION%...
) else (
    echo Building Crystal Relay - Twitch to OSC...
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%BUILD_SCRIPT%"
if errorlevel 1 (
    echo.
    echo Release build failed.
    goto launch_existing
)

call :resolve_version
call :resolve_target_release

if defined TARGET_RELEASE_EXE if exist "%TARGET_RELEASE_EXE%" goto launch_target

call :resolve_latest_release
if defined LATEST_RELEASE_EXE goto launch_latest

echo.
echo Crystal Relay built, but the release executable could not be found.
pause
exit /b 1

:launch_existing
if defined TARGET_RELEASE_EXE if exist "%TARGET_RELEASE_EXE%" goto launch_target
if defined LATEST_RELEASE_EXE goto launch_latest

echo.
echo No built Crystal Relay release was found.
echo Run this launcher again after installing the .NET SDK, or build a release manually.
pause
exit /b 1

:launch_target
echo Launching Crystal Relay - Twitch to OSC v%APP_VERSION%...
if /i "!CRYSTAL_RELAY_LAUNCHER_DRY_RUN!"=="1" (
    echo DRY RUN: %TARGET_RELEASE_EXE%
    exit /b 0
)
start "%APP_NAME%" "%TARGET_RELEASE_EXE%"
exit /b 0

:launch_latest
echo Launching the latest available Crystal Relay release...
if /i "!CRYSTAL_RELAY_LAUNCHER_DRY_RUN!"=="1" (
    echo DRY RUN: %LATEST_RELEASE_EXE%
    exit /b 0
)
start "%APP_NAME%" "%LATEST_RELEASE_EXE%"
exit /b 0

:resolve_version
set "APP_VERSION="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "$projectPath = '%PROJECT_PATH%'; if (Test-Path $projectPath) { [xml]$project = Get-Content -Path $projectPath; $version = $project.Project.PropertyGroup.Version | Select-Object -First 1; if ($version) { $version.Trim() } }"`) do (
    set "APP_VERSION=%%V"
)
exit /b 0

:resolve_target_release
set "TARGET_RELEASE_EXE="
if not defined APP_VERSION exit /b 0
for /f "usebackq delims=" %%E in (`powershell -NoProfile -Command "$version = '%APP_VERSION%'; $versionRoot = Join-Path '%ROOT%' ('Releases\v' + $version); if (Test-Path $versionRoot) { Get-ChildItem -Path $versionRoot -Filter 'CrystalRelayTwitchOsc*.exe' -Recurse -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName }"`) do (
    set "TARGET_RELEASE_EXE=%%E"
)
exit /b 0

:resolve_latest_release
set "LATEST_RELEASE_EXE="
for /f "usebackq delims=" %%E in (`powershell -NoProfile -Command "$releaseRoot = Join-Path '%ROOT%' 'Releases'; if (Test-Path $releaseRoot) { Get-ChildItem -Path $releaseRoot -Filter 'CrystalRelayTwitchOsc-v*.exe' -Recurse -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName }"`) do (
    set "LATEST_RELEASE_EXE=%%E"
)
exit /b 0
