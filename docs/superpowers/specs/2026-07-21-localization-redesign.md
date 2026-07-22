# Localization Redesign — Crystal Relay

## Goal

Redo the entire translation system from the ground up: clean up accumulated file cruft, remove the base/extra file split, rebuild every non-English locale, and keep backward compatibility with all existing `{loc:Translate '...'}` XAML/C# call sites.

## Scope

- Keep the existing lookup-key pattern (mostly English source text plus established semantic keys)
- Keep the existing `LocalizationService.Translate()` / `Format()` / `TranslateExtension` interface unchanged
- Keep all 33 XAML files and 282+ C# call sites untouched

## Changes

### 1. File Structure

Before (per language):
```
Resources\Localization\
  en-US.json           (784 keys)
  en-US.extra.json     (1339 keys)
  en-US.extra.fixed.json  (stale, invalid JSON)
  sv-SE.json.backup    (stale)
  sv-SE.extra.json.backup (stale)
  test_write.txt       (artifact)
  cs-CZ.json           (orphan, unregistered language)
  cs-CZ.extra.json     (orphan)
  ... same for 13 other languages
```

After:
```
Resources\Localization\
  en-US.json           (~2123 keys, single merged source of truth)
  de-DE.json           (same keys as en-US, German translations)
  es-ES.json
  fr-FR.json
  it-IT.json
  ja-JP.json
  ko-KR.json
  pl-PL.json
  pt-BR.json
  ru-RU.json
  sv-SE.json
  th-TH.json
  zh-CN.json
  zh-TW.json
```

**Removed:**
- All `.extra.json` files — content merged into the base file
- `en-US.extra.fixed.json` — stale artifact
- `sv-SE.*.backup` files — stale backups
- `test_write.txt` — artifact
- `cs-CZ.json`, `cs-CZ.extra.json` — orphan language, never exposed in UI

### 2. en-US Master

Produced by merging `en-US.json` + `en-US.extra.json`, deduplicating keys, and verifying:
- No duplicate keys (by ordinal case-sensitive comparison)
- All `{0}`, `{1}` etc. format placeholders are well-formed
- No empty values
- Sorted alphabetically for diff-friendly maintenance

The merged en-US keys and values are kept verbatim. Most keys are source text; established semantic keys retain their separate user-facing English values.

### 3. LocalizationService Changes

Minimal:
- `LoadTranslations(AppLanguage language)` — loads single `{lang}.json` from embedded resources instead of merging base + extra
- Remove `_extraStrings` dictionary and merge logic
- Everything else (`Translate`, `Format`, `TranslateExtension`, `AvailableLanguages`, `Initialize`, `ResolveLanguage`) stays identical

### 4. LocalizationAudit Changes

- Remove base/extra merge logic
- Compare each language file directly against en-US single-file
- Keep existing checks: missing keys, empty values, placeholder mismatches, likely-untranslated
- Keep hardcoded-text detection

### 5. Language List

14 languages in the picker:
| Code | Name |
|------|------|
| en-US | English |
| de-DE | Deutsch |
| es-ES | Español |
| fr-FR | Français |
| it-IT | Italiano |
| ja-JP | 日本語 |
| ko-KR | 한국어 |
| pl-PL | Polski |
| pt-BR | Português (Brasil) |
| ru-RU | Русский |
| sv-SE | Svenska |
| th-TH | ไทย |
| zh-CN | 简体中文 |
| zh-TW | 繁體中文 |

cs-CZ is dropped.

## Translation Pipeline

1. Consolidate en-US master (one-time script)
2. Delete stale files
3. Update `LocalizationService.cs`
4. Update `LocalizationAudit` `Program.cs`
5. Translate each of the 13 locale files directly from:
   - The clean en-US.json
   - A strict translation brief (natural, conversational, informal register, preserve all `{N}` placeholders, keep brand/tech terms in English)
   - Deterministic checks for key coverage, placeholders, UTF-8 integrity, and protected terms
6. Run `LocalizationAudit` across all resulting files
7. Build the app project to verify no regressions
8. Commit

## Quality Requirements

Natural native-speaker review remains the long-term target. For this implementation pass, the user accepted lower-quality machine output with English safety fallbacks where placeholders or protected terms could not be preserved safely.

- Every non-English translation must sound natural and conversational in the target language (informal register: du, tú, tu, du, etc.)
- Brand and technical terms stay in English: `Bits`, `Subs`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `Crystal Relay`, `StreamElements`, `Streamlabs`, `Ko-fi`
- All `{0}`, `{1}`, `{0:N0}`, `{0:0.##}` etc. placeholders preserved exactly — never modified, reordered, or renamed
- Product names, feature brand terms, UI identifiers (`VRC:` prefix, `!world`, `Cheer`) not translated
- Gaming/streaming vocabulary uses terms native speakers actually use, not literal translations

## Success Criteria

- All 14 locale files have identical key coverage, no empty values, and exact placeholder sequences
- App builds successfully with `dotnet build`
- All XAML `{loc:Translate '...'}` references resolve correctly
- All C# `LocalizationService.Translate()` / `Format()` calls continue to work

Zero likely-untranslated and hardcoded-XAML audit findings remain the follow-up quality target after native-language review and the separate XAML localization cleanup.
