# Localization Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redo Crystal Relay's translation system from the ground up — consolidate to single-file-per-language, remove stale artifacts, and re-translate all 13 non-English languages with natural conversational quality.

**Architecture:** Keep the existing source-text-as-key pattern and `LocalizationService.Translate()`/`Format()`/`TranslateExtension` interface untouched. Only change: each language has one `.json` file (not base + extra), `LocalizationService` loads single files, `LocalizationAudit` compares single files. Translations are produced by 13 parallel subagents.

**Tech Stack:** C#, .NET 10, WPF/XAML, System.Text.Json, LocalizationAudit (console app)

## Global Constraints

- All existing `{loc:Translate '...'}` references in 33 XAML files and 282+ C# call sites must continue to work unchanged
- Brand/tech terms stay in English: `Bits`, `Subs`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `Crystal Relay`, `StreamElements`, `Streamlabs`, `Ko-fi`
- All `{0}`, `{1}`, `{0:N0}`, `{0:0.##}` etc. placeholders preserved exactly — never modified, reordered, or renamed
- en-US keys kept verbatim (key == value) — these are the lookup keys
- Informal/friendly register for all non-English translations
- cs-CZ files are dropped (orphan language, never exposed in UI)
- App must build with `dotnet build` after changes

---

### Task 0: Merge en-US and clean stale files

**Files:**
- Create: (none — existing files will be replaced)
- Modify: (replace existing `en-US.json` with merged version)
- Delete: `en-US.extra.json`, `en-US.extra.fixed.json`, all `*.extra.json`, all `*.backup`, `test_write.txt`, `cs-CZ.json`, `cs-CZ.extra.json`

- [ ] **Step 1: Write and run merge script**

Run the following PowerShell to merge en-US base + extra, deduplicate keys, and sort:

```powershell
$base = Get-Content "VrcTwitchOscBridge\Resources\Localization\en-US.json" | ConvertFrom-Json -AsHashTable
$extra = Get-Content "VrcTwitchOscBridge\Resources\Localization\en-US.extra.json" | ConvertFrom-Json -AsHashTable
$merged = [ordered]@{}
foreach ($key in $base.Keys) { $merged[$key] = $base[$key] }
foreach ($key in $extra.Keys) { $merged[$key] = $extra[$key] }
$merged.Keys | Sort-Object | ForEach-Object { $sorted[$_] = $merged[$_] }
$sorted | ConvertTo-Json -Depth 1 | Set-Content "VrcTwitchOscBridge\Resources\Localization\en-US.json" -Encoding UTF8
```

- [ ] **Step 2: Verify merged file**

Check that line count and key count look reasonable:
```powershell
$json = Get-Content "VrcTwitchOscBridge\Resources\Localization\en-US.json" -Raw | ConvertFrom-Json -AsHashTable
Write-Host "Total keys: $($json.Count)"
```
Expected: ~2123 keys (784 base + 1339 extra, minus any exact dupes)

- [ ] **Step 3: Delete stale files**

```powershell
$locDir = "VrcTwitchOscBridge\Resources\Localization"
Remove-Item -LiteralPath (Join-Path $locDir "en-US.extra.json") -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $locDir "en-US.extra.fixed.json") -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $locDir "test_write.txt") -ErrorAction SilentlyContinue
# Remove cs-CZ files
Remove-Item -LiteralPath (Join-Path $locDir "cs-CZ.json") -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $locDir "cs-CZ.extra.json") -ErrorAction SilentlyContinue
# Remove all .extra.json files
Get-ChildItem -LiteralPath $locDir -Filter "*.extra.json" | Remove-Item
# Remove .backup files
Get-ChildItem -LiteralPath $locDir -Filter "*.backup" | Remove-Item
```

- [ ] **Step 4: Verify directory is clean**

Run `Get-ChildItem "VrcTwitchOscBridge\Resources\Localization"` and confirm only files matching `{lang}.json` remain (14 files).

---

### Task 1: Update LocalizationService.cs

**Files:**
- Modify: `VrcTwitchOscBridge\Services\LocalizationService.cs`

**Interfaces:**
- Consumes: None (self-contained)
- Produces: Updated `LocalizationService.LoadTranslations()` that loads single `.json` file

- [ ] **Step 1: Remove extra-merge logic from `LoadTranslations`**

Replace the existing `LoadTranslations` method (lines 156-167):

```csharp
private static IReadOnlyDictionary<string, string> LoadTranslations(AppLanguage language)
{
    if (!ResourceNames.TryGetValue(language, out var resourceFileName))
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    var resourceName = $"{ResourceRoot}.{resourceFileName}";
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
    if (stream is null)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    using var reader = new StreamReader(stream);
    var json = reader.ReadToEnd();
    var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    return strings ?? new Dictionary<string, string>(StringComparer.Ordinal);
}
```

- [ ] **Step 2: Remove `MergeTranslations` private method**

Delete the entire `MergeTranslations` method (lines 169-190).

- [ ] **Step 3: Remove unused `System.IO` using**

`System.IO` was only used by `Path.GetFileNameWithoutExtension` in the removed merge logic. Verify it's not needed elsewhere. If the only `Path` reference was in `MergeTranslations`, remove the import.

- [ ] **Step 4: Build to verify**

```powershell
dotnet build "VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds with no errors. (Will have warnings about unused embedded resources until all language files match.)

---

### Task 2: Update LocalizationAudit Program.cs

**Files:**
- Modify: `LocalizationAudit\Program.cs`

**Interfaces:**
- Consumes: New single-file structure from Tasks 0-1
- Produces: Audit that loads one file per language

- [ ] **Step 1: Update `LoadLanguage` to load single file**

Replace the existing `LoadLanguage` method (lines 119-141):

```csharp
static Dictionary<string, string> LoadLanguage(string root, string cultureName)
{
    var path = Path.Combine(root, $"{cultureName}.json");
    if (!File.Exists(path))
    {
        throw new FileNotFoundException("Missing localization file.", path);
    }

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
        ?? throw new InvalidOperationException($"Localization file is empty: {Path.GetFileName(path)}");
}
```

- [ ] **Step 2: Build the audit project**

```powershell
dotnet build "LocalizationAudit\LocalizationAudit.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 3-15: Translate 13 non-English languages (parallel)

**Each task (3 = de-DE, 4 = es-ES, 5 = fr-FR, 6 = pt-BR, 7 = sv-SE, 8 = it-IT, 9 = ja-JP, 10 = ko-KR, 11 = zh-CN, 12 = zh-TW, 13 = ru-RU, 14 = pl-PL, 15 = th-TH)**

- [ ] **Step 1: Dispatch subagent for `{language}`**

Each subagent receives:
- The full `en-US.json` content (~2123 key-value pairs)
- A strict translation brief (see below)
- The polyglot-reviewer agent type for quality verification

**Subagent prompt template:**

```
Translate this en-US localization file into {LANGUAGE NAME} ({LANG CODE}) for a C# WPF desktop app called Crystal Relay.

Translation rules (MANDATORY):
1. Natural, conversational, informal register (du/tú/tu/du 등)
2. Brand/tech terms stay in English: Bits, Subs, OSC, OSCQuery, VRChat, Twitch, Crystal Relay, StreamElements, Streamlabs, Ko-fi
3. All {0}, {1}, {0:N0}, {0:0.##} etc. placeholders preserved EXACTLY - never modify, reorder, or rename
4. Product names and UI identifiers not translated: VRC: prefix, !world, Cheer
5. Gaming/streaming vocabulary: use terms native speakers actually use, not literal translations
6. Every key MUST be present in the output - no gaps

After writing the file, run polyglot-reviewer on the output to verify naturalness and fix any issues.

Return only the complete translated JSON file content.
```

Each subagent writes the result to `{language}.json` in `VrcTwitchOscBridge\Resources\Localization\`.

All 13 subagents run in parallel.

---

### Task 16: Run localization audit

- [ ] **Step 1: Run audit**

```powershell
dotnet run --project "LocalizationAudit\LocalizationAudit.csproj"
```
Expected: "Localization audit passed." with exit code 0.

If failures exist (missing keys, placeholder mismatches, likely untranslated values), fix the specific language file and re-run.

---

### Task 17: Build app project

- [ ] **Step 1: Build app**

```powershell
dotnet build "VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds with no errors.

---

### Task 18: Commit

- [ ] **Step 1: Stage and commit**

```powershell
git add -A
git commit -m "Redo translation system: single-file locales, fresh translations for 13 languages

- Merged en-US base + extra into single en-US.json
- Removed stale artifacts (backups, .extra files, test_write.txt)
- Removed orphan cs-CZ locale files
- Updated LocalizationService to load single .json files
- Updated LocalizationAudit for single-file comparison
- Re-translated all 13 non-English languages from scratch with natural conversational quality
- AGENTS.md: marked cs-CZ dropped from active language list
```
