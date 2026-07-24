# Localization Natural Translation — Design Doc

## Goal
Retranslate all 13 non-English Crystal Relay localization files from scratch to sound natural, conversational, and informal/friendly — not machine-translated. Fix existing terminology errors and untranslated English fragments.

## Languages
de-DE, es-ES, fr-FR, it-IT, pt-BR, sv-SE, pl-PL, ru-RU, th-TH, ja-JP, ko-KR, zh-CN, zh-TW

## Requirements
- **Register**: Informal/friendly per locale (du/tú/tu/ты/собеседнику/etc.)
- **Sub terminology**: Keep "Sub" as streaming shorthand in all languages
- **Tech brand terms**: Keep English: Bits, Subs, OSC, OSCQuery, VRChat, Twitch, Crystal Relay, StreamElements, Streamlabs, Ko-fi
- **Descriptive text tone**: Informative + friendly
- **Placeholders**: Preserve all `{0}`, `{1:N0}`, etc. exactly
- **No empty values or English copies** in non-English files
- **Fix wrong terminology**: e.g., "sub" → "字幕" (subtitles), "Accounts" → "Berichte/記録/Сведения" instead of correct streaming terms

## Execution Strategy
- 4 batches of 3-4 languages each, dispatched as parallel subagents
- Each agent reads the full en-US.json source + its target language file
- Each agent researches proper streaming/gaming terminology and grammar online for its language
- Each agent rewrites every value naturally
- Final audit pass: run LocalizationAudit project to verify key coverage, placeholder integrity

## Batches
| Batch | Languages | Rationale |
|-------|-----------|-----------|
| 1 | de-DE, fr-FR, es-ES | European core |
| 2 | pt-BR, it-IT, sv-SE | Romance + Scandinavian |
| 3 | pl-PL, ru-RU, th-TH | Slavic + Thai |
| 4 | ja-JP, ko-KR, zh-CN, zh-TW | East Asian |

## Verification
- Run `LocalizationAudit` tool after all files are written
- Build the app project to verify no JSON parse errors
