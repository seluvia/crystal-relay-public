# Localization Natural Retranslation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Naturally retranslate all 13 Crystal Relay localization files from English source, fixing wrong terminology and machine-like phrasing.

**Architecture:** 4 batches of parallel subagents (3-4 languages each), each agent handling one complete language file. Each agent reads en-US.json + target file, researches proper terms online, rewrites every value using informal/friendly register (du/tú/ты/etc.), preserves all `{0}` placeholders and JSON structure. Final audit pass validates all files.

**Tech Stack:** Localization JSON files under `Resources/Localization/`. LocalizationAudit .NET project for verification.

## Global Constraints

- All 13 languages must be retranslated: de-DE, es-ES, fr-FR, it-IT, pt-BR, sv-SE, pl-PL, ru-RU, th-TH, ja-JP, ko-KR, zh-CN, zh-TW
- Source file: `VrcTwitchOscBridge\Resources\Localization\en-US.json` (~2121 keys)
- Target files: same directory, same filename pattern `{locale}.json`
- Format: JSON key-value pairs, keys are English and MUST NOT be changed
- All `{0}`, `{1}`, `{2}`, `{0:N0}`, `{0:0.##}`, `{1}s`, etc. format placeholders MUST be preserved exactly — never modify, reorder, or rename placeholders
- Brand/tech terms that stay in English: `Bits`, `Subs`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `Crystal Relay`, `StreamElements`, `Streamlabs`, `Ko-fi`
- `VRC:` prefix stays as-is in all languages
- `!world` command stays as-is
- `Cheers` stays as-is
- Register: informal/friendly per locale (du for de-DE, tú for es-ES, tu for fr-FR, ты/обращение на 'ты' for ru-RU, du for sv-SE, etc.)
- Streaming/gaming terms should use the natural terms native speakers in that language use, not literal translations
- Terminology within each language must be consistent — pick one term for recurring concepts and stick to it across every key
- No empty localized values
- No English copies accidentally left in non-English files (English-only brand/tech terms are exempt)
- File line count and structure must be preserved (same keys in same order)
- Run LocalizationAudit after all batches complete

---

### Batch 1: de-DE, fr-FR, es-ES (European Core)

**Files to modify:**
- `VrcTwitchOscBridge\Resources\Localization\de-DE.json`
- `VrcTwitchOscBridge\Resources\Localization\fr-FR.json`
- `VrcTwitchOscBridge\Resources\Localization\es-ES.json`

**Source file to read:**
- `VrcTwitchOscBridge\Resources\Localization\en-US.json`

**Approach per language:**
- Read entire en-US.json and target file
- Research online for: common streaming/gaming terms in the target language, informal register grammar rules, correct spelling and phrasing
- Fix wrong terminology found during preview (e.g., French "About Crystal Relay" left in English at fr-FR:32, Spanish similar issues)
- Rewrite every value to sound natural and conversational while preserving meaning
- Keep "Sub" as streaming shorthand, keep "Bits" as-is
- Ensure consistent term usage within each language file

**Verification:**
- JSON must parse valid
- Same number of keys as en-US.json
- All `{0}`, `{1}` etc. placeholders preserved
- No English leftovers (except exempted brand/tech terms)

---

### Batch 2: pt-BR, it-IT, sv-SE (Romance + Scandinavian)

**Files to modify:**
- `VrcTwitchOscBridge\Resources\Localization\pt-BR.json`
- `VrcTwitchOscBridge\Resources\Localization\it-IT.json`
- `VrcTwitchOscBridge\Resources\Localization\sv-SE.json`

**Source file to read:**
- `VrcTwitchOscBridge\Resources\Localization\en-US.json`

**Approach per language:** Same as Batch 1.

---

### Batch 3: pl-PL, ru-RU, th-TH (Slavic + Thai)

**Files to modify:**
- `VrcTwitchOscBridge\Resources\Localization\pl-PL.json`
- `VrcTwitchOscBridge\Resources\Localization\ru-RU.json`
- `VrcTwitchOscBridge\Resources\Localization\th-TH.json`

**Source file to read:**
- `VrcTwitchOscBridge\Resources\Localization\en-US.json`

**Approach per language:** Same as Batch 1.

---

### Batch 4: ja-JP, ko-KR, zh-CN, zh-TW (East Asian)

**Files to modify:**
- `VrcTwitchOscBridge\Resources\Localization\ja-JP.json`
- `VrcTwitchOscBridge\Resources\Localization\ko-KR.json`
- `VrcTwitchOscBridge\Resources\Localization\zh-CN.json`
- `VrcTwitchOscBridge\Resources\Localization\zh-TW.json`

**Source file to read:**
- `VrcTwitchOscBridge\Resources\Localization\en-US.json`

**Approach per language:** Same as Batch 1.

---

### Final Verification Task

**Files:**
- Run: `E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj`
- Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

**Steps:**
1. Run the localization audit and fix any issues found (missing keys, placeholder mismatches, empty values)
2. Build the app project to verify no JSON parse errors
3. Commit all changes
