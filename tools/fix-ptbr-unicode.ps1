$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$basePath = 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization'

$enExtraRaw = [System.IO.File]::ReadAllText("$basePath\en-US.extra.json", [System.Text.Encoding]::UTF8)
$ptMainRaw = [System.IO.File]::ReadAllText("$basePath\pt-BR.json", [System.Text.Encoding]::UTF8)
$ptExtraRaw = [System.IO.File]::ReadAllText("$basePath\pt-BR.extra.json", [System.Text.Encoding]::UTF8)

function Get-JsonKeyValuePairs {
    param([string]$Raw)
    $pairs = [ordered]@{}
    $regex = [regex]::new('"((?:[^"\\]|\\.)*)"\s*:\s*"((?:[^"\\]|\\.)*)"')
    $matches = $regex.Matches($Raw)
    foreach ($m in $matches) {
        $key = $m.Groups[1].Value -replace '\\n', "`n" -replace '\\\"', '"' -replace '\\\\', '\'
        $value = $m.Groups[2].Value -replace '\\n', "`n" -replace '\\\"', '"' -replace '\\\\', '\'
        if (-not $pairs.Contains($key)) {
            $pairs[$key] = $value
        }
    }
    return $pairs
}

$ptMainPairs = Get-JsonKeyValuePairs $ptMainRaw

$lastBrace = $ptExtraRaw.LastIndexOf('}')
$fixedExtraRaw = $ptExtraRaw.Substring(0, $lastBrace + 1)
$ptExtraPairs = Get-JsonKeyValuePairs $fixedExtraRaw
$orphanedRaw = $ptExtraRaw.Substring($lastBrace + 1)
$orphanedPairs = Get-JsonKeyValuePairs $orphanedRaw

foreach ($key in $orphanedPairs.Keys) {
    if (-not $ptExtraPairs.Contains($key)) {
        $ptExtraPairs[$key] = $orphanedPairs[$key]
    }
}

Write-Host "pt-BR main pairs: $($ptMainPairs.Count)"
Write-Host "pt-BR extra pairs (merged): $($ptExtraPairs.Count)"

# Use [char] codes to avoid encoding issues in the script
$em = [string][char]0x2014  # em-dash
$aTilde = [string][char]0x00E3  # ã
$aAcute = [string][char]0x00E1  # á
$eAcute = [string][char]0x00E9  # é
$eCirc = [string][char]0x00EA  # ê
$iAcute = [string][char]0x00ED  # í
$oAcute = [string][char]0x00F3  # ó
$oCirc = [string][char]0x00F4  # ô
$oTilde = [string][char]0x00F5  # õ
$uAcute = [string][char]0x00FA  # ú
$ccedil = [string][char]0x00E7  # ç
$ATilde = [string][char]0x00C3  # Ã
$AAcute = [string][char]0x00C1  # Á
$ECirc = [string][char]0x00CA  # Ê
$IAcute = [string][char]0x00CD  # Í
$OAcute = [string][char]0x00D3  # Ó
$UAcute = [string][char]0x00DA  # Ú
$CCedil = [string][char]0x00C7  # Ç

function Restore-PortugueseAccents {
    param([string]$Text)
    
    if (-not $Text.Contains($em)) {
        return $Text
    }
    
    $result = $Text
    
    # Double em-dash patterns
    $result = $result.Replace("$em${em}o", "${ccedil}${aTilde}o")
    $result = $result.Replace("$em${em}es", "${ccedil}${aTilde}es")
    $result = $result.Replace("$em${em}", "${aTilde}o")
    
    # Common word patterns using [char] codes
    $result = $result.Replace("N${em}o", "N${aTilde}o")
    $result = $result.Replace("n${em}o", "n${aTilde}o")
    $result = $result.Replace("est${em}", "est${aAcute}")
    $result = $result.Replace("Est${em}", "Est${aAcute}")
    $result = $result.Replace("voc${em}", "voc${eCirc}")
    $result = $result.Replace("Voc${em}", "Voc${eCirc}")
    $result = $result.Replace("par${em}metro", "par${aAcute}metro")
    $result = $result.Replace("Par${em}metro", "Par${aAcute}metro")
    $result = $result.Replace("t${em}tulo", "t${iAcute}tulo")
    $result = $result.Replace("T${em}tulo", "T${iAcute}tulo")
    $result = $result.Replace("m${em}ximo", "m${aAcute}ximo")
    $result = $result.Replace("M${em}ximo", "M${aAcute}ximo")
    $result = $result.Replace("m${em}xima", "m${aAcute}xima")
    $result = $result.Replace("M${em}xima", "M${aAcute}xima")
    $result = $result.Replace("m${em}nimo", "m${iAcute}nimo")
    $result = $result.Replace("M${em}nimo", "M${iAcute}nimo")
    $result = $result.Replace("m${em}nima", "m${iAcute}nima")
    $result = $result.Replace("M${em}nima", "M${iAcute}nima")
    $result = $result.Replace("n${em}mero", "n${uAcute}mero")
    $result = $result.Replace("N${em}mero", "N${uAcute}mero")
    $result = $result.Replace("${em}ltimo", "${uAcute}ltimo")
    $result = $result.Replace("${em}ltima", "${uAcute}ltima")
    $result = $result.Replace("${em}nico", "${uAcute}nico")
    $result = $result.Replace("${em}nica", "${uAcute}nica")
    $result = $result.Replace("p${em}blico", "p${uAcute}blico")
    $result = $result.Replace("P${em}blico", "P${uAcute}blico")
    $result = $result.Replace("seguran${em}a", "seguran${ccedil}a")
    $result = $result.Replace("Seguran${em}a", "Seguran${ccedil}a")
    $result = $result.Replace("automa${em}ticamente", "automa${ccedil}amente")
    $result = $result.Replace("r${em}pido", "r${aAcute}pido")
    $result = $result.Replace("r${em}pida", "r${aAcute}pida")
    $result = $result.Replace("f${em}cil", "f${aAcute}cil")
    $result = $result.Replace("F${em}cil", "F${aAcute}cil")
    $result = $result.Replace("poss${em}vel", "poss${iAcute}vel")
    $result = $result.Replace("Poss${em}vel", "Poss${iAcute}vel")
    $result = $result.Replace("dispon${em}vel", "dispon${iAcute}vel")
    $result = $result.Replace("Dispon${em}vel", "Dispon${iAcute}vel")
    $result = $result.Replace("dispon${em}veis", "dispon${iAcute}veis")
    $result = $result.Replace("Dispon${em}veis", "Dispon${iAcute}veis")
    $result = $result.Replace("vis${em}vel", "vis${iAcute}vel")
    $result = $result.Replace("Vis${em}vel", "Vis${iAcute}vel")
    $result = $result.Replace("n${em}vel", "n${iAcute}vel")
    $result = $result.Replace("N${em}vel", "N${iAcute}vel")
    $result = $result.Replace("n${em}veis", "n${iAcute}veis")
    $result = $result.Replace("N${em}veis", "N${iAcute}veis")
    $result = $result.Replace("usu${em}rio", "usu${aAcute}rio")
    $result = $result.Replace("Usu${em}rio", "Usu${aAcute}rio")
    $result = $result.Replace("usu${em}rios", "usu${aAcute}rios")
    $result = $result.Replace("Usu${em}rios", "Usu${aAcute}rios")
    $result = $result.Replace("at${em}", "at${eAcute}")
    $result = $result.Replace("At${em}", "At${eAcute}")
    $result = $result.Replace("sa${em}da", "sa${iAcute}da")
    $result = $result.Replace("Sa${em}da", "Sa${iAcute}da")
    $result = $result.Replace("in${em}cio", "in${iAcute}cio")
    $result = $result.Replace("In${em}cio", "In${iAcute}cio")
    $result = $result.Replace("p${em}gina", "p${aAcute}gina")
    $result = $result.Replace("P${em}gina", "P${aAcute}gina")
    $result = $result.Replace("p${em}ginas", "p${aAcute}ginas")
    $result = $result.Replace("P${em}ginas", "P${aAcute}ginas")
    $result = $result.Replace("bot${em}o", "bot${aTilde}o")
    $result = $result.Replace("Bot${em}o", "Bot${aTilde}o")
    $result = $result.Replace("bot${em}es", "bot${oTilde}es")
    $result = $result.Replace("Bot${em}es", "Bot${oTilde}es")
    $result = $result.Replace("prefer${em}ncia", "prefer${eCirc}ncia")
    $result = $result.Replace("Prefer${em}ncia", "Prefer${eCirc}ncia")
    $result = $result.Replace("refer${em}ncia", "refer${eCirc}ncia")
    $result = $result.Replace("Refer${em}ncia", "Refer${eCirc}ncia")
    $result = $result.Replace("configur${em}vel", "configur${aAcute}vel")
    $result = $result.Replace("aceit${em}vel", "aceit${aAcute}vel")
    $result = $result.Replace("confi${em}vel", "confi${aAcute}vel")
    $result = $result.Replace("r${em}tulo", "r${oAcute}tulo")
    $result = $result.Replace("R${em}tulo", "R${oAcute}tulo")
    $result = $result.Replace("Pr${em}", "Pr${eAcute}")
    $result = $result.Replace("pr${em}", "pr${eAcute}")
    $result = $result.Replace("diagn${em}stico", "diagn${oAcute}stico")
    $result = $result.Replace("Diagn${em}stico", "Diagn${oAcute}stico")
    $result = $result.Replace("hist${em}rico", "hist${oAcute}rico")
    $result = $result.Replace("Hist${em}rico", "Hist${oAcute}rico")
    $result = $result.Replace("autom${em}tico", "autom${aAcute}tico")
    $result = $result.Replace("Autom${em}tico", "Autom${aAcute}tico")
    $result = $result.Replace("autom${em}tica", "autom${aAcute}tica")
    $result = $result.Replace("Autom${em}tica", "Autom${aAcute}tica")
    $result = $result.Replace("espec${em}fico", "espec${iAcute}fico")
    $result = $result.Replace("Espec${em}fico", "Espec${iAcute}fico")
    $result = $result.Replace("espec${em}ficos", "espec${iAcute}ficos")
    $result = $result.Replace("Espec${em}ficos", "Espec${iAcute}ficos")
    $result = $result.Replace("${em} ", "${aTilde} ")
    $result = $result.Replace(" ${em} ", " ${aTilde} ")
    $result = $result.Replace("${em}s", "${aTilde}s")
    $result = $result.Replace("${em}a", "${aAcute}a")
    $result = $result.Replace("${em}e", "${eAcute}e")
    $result = $result.Replace("${em}o", "${aAcute}o")
    
    return $result
}

# Get en-US extra keys in order
$enExtraKeys = [System.Collections.Generic.List[string]]::new()
$enExtraRegex = [regex]::new('"((?:[^"\\]|\\.)*)"\s*:\s*"')
$enExtraMatches = $enExtraRegex.Matches($enExtraRaw)
foreach ($m in $enExtraMatches) {
    $key = $m.Groups[1].Value -replace '\\n', "`n" -replace '\\\"', '"' -replace '\\\\', '\'
    if (-not $enExtraKeys.Contains($key)) {
        $enExtraKeys.Add($key)
    }
}

Write-Host "en-US extra keys (ordered): $($enExtraKeys.Count)"

# Build final pairs
$finalPairs = [ordered]@{}
$missingKeys = [System.Collections.Generic.List[string]]::new()

foreach ($key in $enExtraKeys) {
    if ($ptMainPairs.Contains($key)) {
        $finalPairs[$key] = $ptMainPairs[$key]
    } elseif ($ptExtraPairs.Contains($key)) {
        $corrupted = $ptExtraPairs[$key]
        $fixed = Restore-PortugueseAccents $corrupted
        $finalPairs[$key] = $fixed
    } else {
        $missingKeys.Add($key)
    }
}

Write-Host "Missing keys: $($missingKeys.Count)"
foreach ($k in $missingKeys) { Write-Host "  MISSING: $k" }

# Add missing keys with proper translations
$translations = @{
    "Count Cash Payments" = "Contar Pagamentos em Dinheiro"
    "Stop Sale" = "Parar Promoção"
    "Points per progress" = "Pontos por progresso"
    "Progress ratio (cents per 1 progress)" = "Proporção de progresso (centavos por 1 de progresso)"
    "At a ratio of " = "Com uma proporção de "
    " cents per progress, each `$1 adds " = " centavos por progresso, cada `$1 adiciona "
    " progress." = " de progresso."
    "Connected payment services feed into Fire Sale progress when Count Cash Payments is enabled above." = "Os serviços de pagamento conectados alimentam o progresso da Promoção Relâmpago quando Contar Pagamentos em Dinheiro está ativado acima."
    "Return Behavior" = "Comportamento de Retorno"
    "Return to Global Return Avatar" = "Retornar ao Avatar de Retorno Global"
    "Return to Previous Avatar" = "Retornar ao Avatar Anterior"
    "Permanent No Return" = "Permanente (Sem Retorno)"
    "Will return to the avatar you were wearing before this swap" = "Vai retornar ao avatar que você estava usando antes desta troca"
}

foreach ($key in $missingKeys) {
    if ($translations.ContainsKey($key)) {
        $finalPairs[$key] = $translations[$key]
    } else {
        $finalPairs[$key] = $key
    }
}

# Write the fixed file with proper UTF-8 encoding
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("{")
$keys = @($finalPairs.Keys)
for ($i = 0; $i -lt $keys.Count; $i++) {
    $key = $keys[$i]
    $value = $finalPairs[$key]
    $escapedKey = $key -replace '\\', '\\\\' -replace '"', '\"' -replace "`n", '\n'
    $escapedValue = $value -replace '\\', '\\\\' -replace '"', '\"' -replace "`n", '\n'
    $comma = if ($i -lt $keys.Count - 1) { "," } else { "" }
    [void]$sb.AppendLine("  `"$escapedKey`": `"$escapedValue`"$comma")
}
[void]$sb.AppendLine("}")

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText("$basePath\pt-BR.extra.json", $sb.ToString(), $utf8NoBom)

Write-Host "`nFixed pt-BR.extra.json written with $($finalPairs.Count) keys"
