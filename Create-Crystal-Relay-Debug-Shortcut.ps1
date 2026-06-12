$ErrorActionPreference = 'Stop'

$repoRoot = 'E:\!!!Program to work on\Proper Crystal Relay'
$debugExe = Join-Path $repoRoot 'VrcTwitchOscBridge\bin\Debug\net10.0-windows\CrystalRelayTwitchOsc.exe'
$shortcutPath = Join-Path $repoRoot 'Crystal Relay Debug.lnk'

if (-not (Test-Path -LiteralPath $debugExe))
{
    Write-Host "Debug executable not found: $debugExe" -ForegroundColor Red
    Write-Host "Build the project in Debug configuration first:" -ForegroundColor Yellow
    Write-Host "  dotnet build `"$($repoRoot)\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj`""
    exit 1
}

$WshShell = New-Object -ComObject WScript.Shell
$shortcut = $WshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $debugExe
$shortcut.WorkingDirectory = (Split-Path -Parent $debugExe)
$shortcut.Description = "Crystal Relay DEBUG Build"
$shortcut.IconLocation = $debugExe
$shortcut.Save()

Write-Host "Created shortcut: $shortcutPath" -ForegroundColor Green
Write-Host "Target: $debugExe"
