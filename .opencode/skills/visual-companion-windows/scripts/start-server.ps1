<#
.SYNOPSIS
  Windows-native launcher for the brainstorming visual companion server.
.DESCRIPTION
  PowerShell port of scripts/start-server.sh from the upstream superpowers
  package. Use this on Windows instead of the .sh script - invoking the
  .sh from PowerShell causes Windows to open it in the default file
  association editor (typically VS Code).
.PARAMETER ProjectDir
  Store session files under <ProjectDir>/.superpowers/brainstorm/ instead
  of the system temp folder. Files persist after the server stops.
.PARAMETER Host
  Host/interface to bind (default: 127.0.0.1).
.PARAMETER UrlHost
  Hostname shown in the returned URL JSON (default: localhost).
#>
[CmdletBinding()]
param(
    [string]$ProjectDir,
    [Alias('Host')]
    [string]$BindHost = '127.0.0.1',
    [string]$UrlHost = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($UrlHost)) {
    if ($BindHost -eq '127.0.0.1' -or $BindHost -eq 'localhost') {
        $UrlHost = 'localhost'
    } else {
        $UrlHost = $BindHost
    }
}

# Generate unique session directory
$sessionId = "$PID-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"

if (-not [string]::IsNullOrEmpty($ProjectDir)) {
    if (-not (Test-Path -LiteralPath $ProjectDir)) {
        Write-Output "{""error"": ""--project-dir path does not exist: $ProjectDir""}"
        exit 1
    }
    $ProjectDir = (Resolve-Path -LiteralPath $ProjectDir).Path
    $sessionRoot = Join-Path $ProjectDir '.superpowers/brainstorm'
    $sessionDir = Join-Path $sessionRoot $sessionId
} else {
    $tempRoot = [System.IO.Path]::GetTempPath().TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $sessionDir = Join-Path $tempRoot "brainstorm-$sessionId"
}

$contentDir = Join-Path $sessionDir 'content'
$stateDir = Join-Path $sessionDir 'state'
New-Item -ItemType Directory -Path $contentDir, $stateDir -Force | Out-Null

# Locate server.cjs in the upstream superpowers package.
# Walk up from the script looking for a local node_modules install, then
# fall back to the well-known opencode packages cache (e.g. %USERPROFILE%
# \.cache\opencode\packages on Windows) where opencode stores installed
# git-based plugins like the upstream superpowers package.
$serverCjs = $null
$probe = $PSScriptRoot
for ($i = 0; $i -lt 8; $i++) {
    $candidate = Join-Path $probe 'node_modules/superpowers/skills/brainstorming/scripts/server.cjs'
    if (Test-Path -LiteralPath $candidate) {
        $serverCjs = (Resolve-Path -LiteralPath $candidate).Path
        break
    }
    $parent = Split-Path -Parent $probe
    if ([string]::IsNullOrEmpty($parent) -or $parent -eq $probe) { break }
    $probe = $parent
}

if (-not $serverCjs) {
    $cacheRoots = @()
    if (-not [string]::IsNullOrEmpty($env:USERPROFILE)) {
        $cacheRoots += (Join-Path $env:USERPROFILE '.cache\opencode\packages')
    }
    $cacheRoots += @(
        'C:\Users\screm\.cache\opencode\packages'
        'C:\cache\opencode\packages'
    )
    foreach ($root in $cacheRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $matches = Get-ChildItem -LiteralPath $root -Recurse -Filter 'server.cjs' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like '*\node_modules\superpowers\skills\brainstorming\scripts\server.cjs' } |
            Select-Object -First 1 -ExpandProperty FullName
        if ($matches) {
            $serverCjs = $matches
            break
        }
    }
}

if (-not $serverCjs) {
    Write-Output "{""error"": ""Could not locate server.cjs. superpowers package not found in node_modules or opencode packages cache.""}"
    exit 1
}

# Find node.exe
$nodeExe = (Get-Command node.exe -ErrorAction SilentlyContinue)?.Source
if (-not $nodeExe) {
    Write-Output "{""error"": ""node.exe not found in PATH. Install Node.js or add it to PATH.""}"
    exit 1
}

# Launch the server detached so this script can return.
# Stdout/stderr are intentionally not captured — the script block event
# handlers that .NET's Process expects cannot run on a non-runspace
# background thread in PowerShell 7+, and we don't need the child's
# output: server.cjs already writes its own server-info file when ready.
$logFile = Join-Path $stateDir 'server.log'
"" | Set-Content -LiteralPath $logFile -Encoding ASCII
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $nodeExe
$startInfo.Arguments = ('"' + $serverCjs + '" 1>>"' + $logFile + '" 2>>"' + $logFile + '"')
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.EnvironmentVariables['BRAINSTORM_DIR'] = $sessionDir
$startInfo.EnvironmentVariables['BRAINSTORM_HOST'] = $BindHost
$startInfo.EnvironmentVariables['BRAINSTORM_URL_HOST'] = $UrlHost
$startInfo.EnvironmentVariables['BRAINSTORM_OWNER_PID'] = "$PID"

$proc = [System.Diagnostics.Process]::Start($startInfo)

# Persist the Node PID for the stopper.
$pidFile = Join-Path $stateDir 'server.pid'
Set-Content -LiteralPath $pidFile -Value "$($proc.Id)" -Encoding ASCII

# Poll for server-info (written by server.cjs when ready).
$infoPath = Join-Path $stateDir 'server-info'
$deadline = (Get-Date).AddSeconds(5)
$info = $null
while ((Get-Date) -lt $deadline) {
    if ($proc.HasExited) { break }
    if (Test-Path -LiteralPath $infoPath) {
        $info = Get-Content -LiteralPath $infoPath -Raw
        break
    }
    Start-Sleep -Milliseconds 100
}

if ($info) {
    Write-Output ($info.Trim())
    exit 0
}

if ($proc.HasExited) {
    Write-Output "{""error"": ""Server exited before becoming ready. See $logFile""}"
    exit 1
}

Write-Output "{""error"": ""Server failed to start within 5 seconds""}"
try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
exit 1
