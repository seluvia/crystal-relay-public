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

function Restore-PortugueseAccents {
    param([string]$Text)
    
    if (-not $Text.Contains([char]0x2014)) {
        return $Text
    }
    
    $result = $Text
    $em = [string][char]0x2014
    
    # Double em-dash patterns (represent çã / çõ)
    $result = $result.Replace("${em}${em}o", "ção")
    $result = $result.Replace("${em}${em}es", "ções")
    $result = $result.Replace("${em}${em}", "ão")
    
    # Now handle remaining single em-dashes using context
    # Replace specific known patterns
    $replacements = @(
        @("${em}o ", "ão "), @("${em}o.", "ão."), @("${em}o,", "ão,"),
        @("N${em}o", "Não"), @("n${em}o", "não"),
        @("est${em}", "está"), @("Est${em}", "Está"),
        @("voc${em}", "você"), @("Voc${em}", "Você"),
        @("par${em}metro", "parâmetro"), @("Par${em}metro", "Parâmetro"),
        @("t${em}tulo", "título"), @("T${em}tulo", "Título"),
        @("m${em}ximo", "máximo"), @("M${em}ximo", "Máximo"),
        @("m${em}xima", "máxima"), @("M${em}xima", "Máxima"),
        @("m${em}nimo", "mínimo"), @("M${em}nimo", "Mínimo"),
        @("m${em}nima", "mínima"), @("M${em}nima", "Mínima"),
        @("n${em}mero", "número"), @("N${em}mero", "Número"),
        @("${em}ltimo", "último"), @("${em}ltima", "última"),
        @("${em}nico", "único"), @("${em}nica", "única"),
        @("p${em}blico", "público"), @("P${em}blico", "Público"),
        @("seguran${em}a", "segurança"), @("Seguran${em}a", "Segurança"),
        @("automa${em}ticamente", "automaticamente"),
        @("r${em}pido", "rápido"), @("r${em}pida", "rápida"),
        @("f${em}cil", "fácil"), @("F${em}cil", "Fácil"),
        @("poss${em}vel", "possível"), @("Poss${em}vel", "Possível"),
        @("dispon${em}vel", "disponível"), @("Dispon${em}vel", "Disponível"),
        @("dispon${em}veis", "disponíveis"), @("Dispon${em}veis", "Disponíveis"),
        @("vis${em}vel", "visível"), @("Vis${em}vel", "Visível"),
        @("n${em}vel", "nível"), @("N${em}vel", "Nível"),
        @("n${em}veis", "níveis"), @("N${em}veis", "Níveis"),
        @("usu${em}rio", "usuário"), @("Usu${em}rio", "Usuário"),
        @("usu${em}rios", "usuários"), @("Usu${em}rios", "Usuários"),
        @("at${em}", "até"), @("At${em}", "Até"),
        @("sa${em}da", "saída"), @("Sa${em}da", "Saída"),
        @("in${em}cio", "início"), @("In${em}cio", "Início"),
        @("p${em}gina", "página"), @("P${em}gina", "Página"),
        @("p${em}ginas", "páginas"), @("P${em}ginas", "Páginas"),
        @("bot${em}o", "botão"), @("Bot${em}o", "Botão"),
        @("bot${em}es", "botões"), @("Bot${em}es", "Botões"),
        @("prefer${em}ncia", "preferência"), @("Prefer${em}ncia", "Preferência"),
        @("refer${em}ncia", "referência"), @("Refer${em}ncia", "Referência"),
        @("configur${em}vel", "configurável"),
        @("aceit${em}vel", "aceitável"),
        @("confi${em}vel", "confiável"),
        @("r${em}tulo", "rótulo"), @("R${em}tulo", "Rótulo"),
        @("Pr${em}", "Pré"), @("pr${em}", "pré"),
        @("diagn${em}stico", "diagnóstico"), @("Diagn${em}stico", "Diagnóstico"),
        @("hist${em}rico", "histórico"), @("Hist${em}rico", "Histórico"),
        @("autom${em}tico", "automático"), @("Autom${em}tico", "Automático"),
        @("autom${em}tica", "automática"), @("Autom${em}tica", "Automática"),
        @("espec${em}fico", "específico"), @("Espec${em}fico", "Específico"),
        @("espec${em}ficos", "específicos"), @("Espec${em}ficos", "Específicos"),
        @("m${em}o ", "mão "), @("m${em}o.", "mão."),
        @("${em} ", "à "),
        @(" ${em} ", " à "),
        @("${em}s", "ãs"),
        @("${em}o", "ão"),
        @("${em}e", "ãe"),
        @("${em}a", "ãa")
    )
    
    foreach ($pair in $replacements) {
        $result = $result.Replace($pair[0], $pair[1])
    }
    
    # Final fallback: replace any remaining em-dashes with ã
    $result = $result.Replace([string][char]0x2014, "ã")
    
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
$fixedCount = 0
$fromMainCount = 0

foreach ($key in $enExtraKeys) {
    if ($ptMainPairs.Contains($key)) {
        $finalPairs[$key] = $ptMainPairs[$key]
        $fromMainCount++
    } elseif ($ptExtraPairs.Contains($key)) {
        $corrupted = $ptExtraPairs[$key]
        $fixed = Restore-PortugueseAccents $corrupted
        $finalPairs[$key] = $fixed
        $fixedCount++
    } else {
        $missingKeys.Add($key)
    }
}

Write-Host "From pt-BR main: $fromMainCount"
Write-Host "Fixed from pt-BR extra: $fixedCount"
Write-Host "Missing keys: $($missingKeys.Count)"
foreach ($k in $missingKeys) { Write-Host "  MISSING: $k" }

# Add missing keys
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

# Write the fixed file
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
