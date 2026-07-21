$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$path = 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\pt-BR.extra.json'
$bytes = [System.IO.File]::ReadAllBytes($path)
Write-Host "File size: $($bytes.Length) bytes"
Write-Host "First 3 bytes: $($bytes[0]) $($bytes[1]) $($bytes[2])"

# Read as UTF-8 and show specific lines
$text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
$lines = $text.Split([char]10)

Write-Host "`nLine 3 (sample):"
Write-Host $lines[2]
Write-Host "`nLine 208 (em-dash issue):"
Write-Host $lines[207]
Write-Host "`nLine 209:"
Write-Host $lines[208]
Write-Host "`nLine 1007:"
Write-Host $lines[1006]

# Count em-dashes
$emCount = ([regex]::Matches($text, [char]0x2014)).Count
Write-Host "`nTotal em-dashes remaining: $emCount"

# Check for mojibake patterns (sign of double-encoding)
$mojibake = ([regex]::Matches($text, 'Ã')).Count
Write-Host "Mojibake patterns (Ã): $mojibake"
