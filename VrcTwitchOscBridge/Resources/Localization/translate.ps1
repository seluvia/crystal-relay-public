$localizationRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appRoot = Split-Path -Parent (Split-Path -Parent $localizationRoot)
Add-Type -Path (Join-Path $appRoot 'bin\Debug\net10.0-windows\Newtonsoft.Json.dll')
$enPath = Join-Path $localizationRoot 'en-US.json'
$zhPath = Join-Path $localizationRoot 'zh-CN.json'
$enContent = [System.IO.File]::ReadAllText($enPath, [System.Text.Encoding]::UTF8)
$zhContent = [System.IO.File]::ReadAllText($zhPath, [System.Text.Encoding]::UTF8)
$dictType = [System.Collections.Generic.Dictionary[string,object]]
$enDict = [Newtonsoft.Json.JsonConvert]::DeserializeObject($enContent, $dictType)
$zhDict = [Newtonsoft.Json.JsonConvert]::DeserializeObject($zhContent, $dictType)

function Add-Translations {
    param($dict)
    $t = [System.Collections.Generic.Dictionary[string,string]]::new()
