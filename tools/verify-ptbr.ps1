$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$basePath = 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization'
$raw = [System.IO.File]::ReadAllText("$basePath\pt-BR.extra.json", [System.Text.Encoding]::UTF8)

# Check if valid JSON
try {
    $null = $raw | ConvertFrom-Json
    Write-Host "JSON is valid"
} catch {
    Write-Host "JSON is INVALID: $($_.Exception.Message)"
}

# Check for remaining em-dashes
$emCount = ([regex]::Matches($raw, [char]0x2014)).Count
Write-Host "Remaining em-dashes: $emCount"

# Show first 20 lines to check quality
$lines = $raw.Split("`n")
Write-Host "`nFirst 30 lines:"
for ($i = 0; $i -lt [Math]::Min(30, $lines.Count); $i++) {
    Write-Host $lines[$i]
}

# Search for any obviously wrong patterns
Write-Host "`nSearching for remaining issues..."
$issues = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line.Contains([char]0x2014)) {
        $issues += "Line $($i+1): $line"
    }
}
Write-Host "Lines with em-dashes: $($issues.Count)"
foreach ($issue in $issues | Select-Object -First 20) {
    Write-Host "  $issue"
}
