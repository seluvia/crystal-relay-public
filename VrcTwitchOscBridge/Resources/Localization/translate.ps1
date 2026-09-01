$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Add-Type -Path (Join-Path $projectRoot 'VrcTwitchOscBridge\bin\Debug\net10.0-windows\Newtonsoft.Json.dll')
$enPath = Join-Path $PSScriptRoot 'en-US.json'
$zhPath = Join-Path $PSScriptRoot 'zh-CN.json'
$enContent = [System.IO.File]::ReadAllText($enPath, [System.Text.Encoding]::UTF8)
$zhContent = [System.IO.File]::ReadAllText($zhPath, [System.Text.Encoding]::UTF8)
$dictType = [System.Collections.Generic.Dictionary[string,object]]
$enDict = [Newtonsoft.Json.JsonConvert]::DeserializeObject($enContent, $dictType)
$zhDict = [Newtonsoft.Json.JsonConvert]::DeserializeObject($zhContent, $dictType)

function Add-Translations {
    param($dict)
    $t = [System.Collections.Generic.Dictionary[string,string]]::new()
