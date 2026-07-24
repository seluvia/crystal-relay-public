Add-Type -Path 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\bin\Debug\net10.0-windows\Newtonsoft.Json.dll'
$enPath = 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\en-US.json'
$zhPath = 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\zh-CN.json'
$enContent = [System.IO.File]::ReadAllText($enPath, [System.Text.Encoding]::UTF8)
$zhContent = [System.IO.File]::ReadAllText($zhPath, [System.Text.Encoding]::UTF8)
$dictType = [System.Collections.Generic.Dictionary[string,object]]
$enDict = [Newtonsoft.Json.JsonConvert]::DeserializeObject($enContent, $dictType)
$zhDict = [Newtonsoft.Json.JsonConvert]::DeserializeObject($zhContent, $dictType)

function Add-Translations {
    param($dict)
    $t = [System.Collections.Generic.Dictionary[string,string]]::new()
