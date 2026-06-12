# Avatar Set UI Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Strip the current inline Avatar Set list + editor from `MainWindow.xaml` and ship a fresh themed secondary window (`AvatarSetsManagerWindow.xaml`) with a card-grid (280x320 cards showing the VRChat avatar profile icon) + slide-in editor. The runtime engine, models, persistence, Twitch reward sync, wardrobe logic, and master-profile selection stay completely intact. Every other Crystal Relay system is untouched.

**Architecture:** Demolition first (kill the inline list pane + editor pane + unused visibility flags), then build the new window in four vertical layers (window shell + empty state + card grid + slide-in editor). The runtime engine in `BridgeCoordinator` and the persistence in `AppSettings.AvatarProfiles` are never touched. The new `AvatarSetsManagerViewModel` is a Bridge VM that observes `MainWindowViewModel.AvatarRuleProfiles` and calls the existing `MainWindowViewModel` commands for editing. Image loading reuses `AvatarImageService` as-is.

**Tech Stack:** C# .NET 10 / `net10.0-windows`, WPF + XAML, `CommunityToolkit.Mvvm` for observable VMs, custom themed window chrome (`shell:WindowChrome` with `WindowStyle="None"`), `DynamicResource` brushes for theme palettes, `loc:Translate` markup extension for localization, `ICollectionView` for filter/sort/search. No new NuGet packages. No new external services.

**Reference spec:** `docs/superpowers/specs/2026-06-11-avatar-set-ui-rework-design.md`

**Already done before this plan starts:**
- Raw source backup should be created before broad code changes: `Backup-Crystal-Relay-Project.ps1` if the user requests it.

---

## File Map

### Created files
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` - the new themed manager window
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs` - code-behind (lifecycle, close, drag)
- `VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs` - window-level VM (filter, sort, search, editor state, commands)
- `VrcTwitchOscBridge/ViewModels/AvatarSetCardViewModel.cs` - per-card wrapper (image load, derived pills, click commands)
- `VrcTwitchOscBridge/Models/AvatarSetsFilterMode.cs` - enum: All, Active, Disabled, LiveNow, Master
- `VrcTwitchOscBridge/Models/AvatarSetsSortMode.cs` - enum: ByName, ByStatus, RecentlyEdited

### Modified files
- `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` - add the 6 new files to `<Page>` and `<Compile>` item groups (project uses `EnableDefaultItems=false`)
- `VrcTwitchOscBridge/MainWindow.xaml` - remove inline Avatar Set list pane + editor pane + empty state; add Manage button + summary text
- `VrcTwitchOscBridge/MainWindowViewModel.cs` - add `OpenAvatarSetsManagerCommand`, `AvatarSetSummaryText`; remove `IsViewingAvatarTriggers`, `IsViewingMasterAvatar`, `SelectedAvatarSetupTitle`, `SelectedAvatarProfileStatusText` (only if no other XAML binds to them)
- `VrcTwitchOscBridge/Resources/Localization/en-US.json` (or `en-US.extra.json` - whichever is the source file in this project) - add 38 new keys
- All other `*.extra.json` localization files (matching translations for the 38 keys)

### Untouched (do NOT modify)
- `VrcTwitchOscBridge/Models/AvatarTriggerProfile.cs`
- `VrcTwitchOscBridge/Models/TriggerRule.cs`
- `VrcTwitchOscBridge/Models/WardrobeOutfit.cs`
- `VrcTwitchOscBridge/Models/WardrobeSnapshotParam.cs`
- `VrcTwitchOscBridge/Models/AppSettings.cs` (the `AvatarProfiles` collection shape is preserved)
- `VrcTwitchOscBridge/Services/AvatarImageService.cs` (reused as-is)
- `VrcTwitchOscBridge/Services/AvatarPickerService.cs` (reused as-is)
- `VrcTwitchOscBridge/AvatarPickerWindow.xaml`/`.cs` (reused as-is)
- `VrcTwitchOscBridge/Services/VrChatApiClient.cs`
- `VrcTwitchOscBridge/Services/VrChatApiRoutes.cs`
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (runtime hot path)
- `VrcTwitchOscBridge/Services/SettingsStore.cs` (no schema migration)
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` (snapshot shape)
- `oscquery-lib/` (vendored library)
- Avatar Change, Power Up, Supporter Growth, Supporter Override, Universal Triggers, Avatar Scaling, Movement Redeems, Bits + Subs overrides, Reward Fire Sale, Cash Payments, Twitch Chatbox, About page - all preserved unchanged

---

## Verification Rules

- Crystal Relay has no automated UI test suite. The standard verification step is `dotnet build` + manual launch.
- Standard build: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo`
- Standard manual launch: `E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat`
- Localization audit: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore`
- Do NOT run `git commit` unless the user explicitly requests a commit. Per AGENTS.md, all commits must be user-initiated.
- After code changes, the project file (`VrcTwitchOscBridge.csproj`) is the source of truth for `<Version>`. Do not bump it during this rework - this is internal UI work, not a release.
- After code changes that affect runtime behavior or XAML, build the app project directly (the `.slnx` is unreliable per AGENTS.md).

---

## Task 1: Locate the source localization file

**Files:** None modified - research only.

The localization files live under `VrcTwitchOscBridge/Resources/Localization/`. The structure in this project is one base `.json` per language plus optional `.extra.json` overlays. We need to know which file is the source-of-truth `en-US` file before adding new keys.

- [ ] **Step 1: List the localization directory**

Run (PowerShell):
```powershell
Get-ChildItem -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization" -Filter "*.json" | Select-Object Name, Length
```

Expected: list of `.json` files including at least `en-US.json` and one or more `.extra.json` files for other languages (de-DE, es-ES, fr-FR, ja-JP, ko-KR, pt-BR, ru-RU, zh-CN, etc.).

- [ ] **Step 2: Identify the en-US source file**

Open `en-US.json` (or whichever file is named without `.extra`). Look for the pattern - is the file the canonical source that other languages extend, or is `en-US.extra.json` the source and `en-US.json` the base?

In Crystal Relay, the convention is:
- `en-US.json` = base English source
- `en-US.extra.json` = English-only additions
- `de-DE.json` + `de-DE.extra.json` = German base + additions
- The localization audit merges both.

Confirm the pattern by reading the first 20 lines of `en-US.json` and `en-US.extra.json` and comparing.

- [ ] **Step 3: List all other language files**

Run (PowerShell):
```powershell
Get-ChildItem -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization" -Filter "*.json" | Where-Object { $_.Name -notin @("en-US.json", "en-US.extra.json") } | Select-Object -ExpandProperty Name
```

Expected: list of language files like `de-DE.json`, `es-ES.json`, etc.

Note: the matching `.extra.json` for each base file will also need new keys. Record the full list. We'll add new keys to:
- `en-US.json` (and `en-US.extra.json` if it's the source)
- `en-US.extra.json` (English additions)
- One `.json` and one `.extra.json` per non-English language

Record the full file list here for reference in later tasks: `__LANG_FILES__`

---

## Task 2: Create the AvatarSetsFilterMode and AvatarSetsSortMode enums

**Files:**
- Create: `VrcTwitchOscBridge/Models/AvatarSetsFilterMode.cs`
- Create: `VrcTwitchOscBridge/Models/AvatarSetsSortMode.cs`

- [ ] **Step 1: Create the filter mode enum**

Create `VrcTwitchOscBridge/Models/AvatarSetsFilterMode.cs` with the following content:

```csharp
namespace VrcTwitchOscBridge.Models;

public enum AvatarSetsFilterMode
{
    All,
    Active,
    Disabled,
    LiveNow,
    Master
}
```

- [ ] **Step 2: Create the sort mode enum**

Create `VrcTwitchOscBridge/Models/AvatarSetsSortMode.cs` with the following content:

```csharp
namespace VrcTwitchOscBridge.Models;

public enum AvatarSetsSortMode
{
    ByName,
    ByStatus,
    RecentlyEdited
}
```

- [ ] **Step 3: Add the new files to the csproj**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`. Find the `<ItemGroup>` containing the other `<Compile Include="Models\...">` entries. Search for `Models\AvatarTriggerProfile.cs` to locate the right group. Add two new entries in the same group:

```xml
<Compile Include="Models\AvatarSetsFilterMode.cs" />
<Compile Include="Models\AvatarSetsSortMode.cs" />
```

If the csproj uses a different pattern (e.g., wildcard globbing or a different file ordering), match the existing pattern. The project uses `EnableDefaultCompileItems=false`, so each new `.cs` file MUST be explicitly listed or it will not build.

- [ ] **Step 4: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The two new enums compile and are referenced by no other code yet (that's fine - they will be used in later tasks).

---

## Task 3: Add the 38 en-US localization keys

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.json` (and/or `en-US.extra.json` per the convention identified in Task 1)
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` (English additions)

- [ ] **Step 1: Open the en-US source file**

Based on the convention identified in Task 1, open the file(s) where new English keys belong. Crystal Relay's pattern (per AGENTS.md) is:
- Add new `en-US` source keys to `en-US.json` (or `en-US.extra.json` if that's the source)
- For keys that apply only to this feature (not replacing existing keys), they can go in `en-US.extra.json` to keep the diff small

For this task, add the 38 new keys to the en-US source file. Use a JSON-compatible editor and preserve the file's existing structure (formatting, indentation, key ordering).

- [ ] **Step 2: Add the 38 new keys**

Add the following JSON object entries. Place them at the end of the existing top-level object, just before the closing `}`. Preserve the file's indentation style (likely 2 or 4 spaces, no tabs).

```json
"Avatar Sets Manager Title": "Avatar Sets",
"Avatar Sets Subtitle Format": "{0} total • {1} active • {2} need avatar",
"Avatar Sets Empty Title": "Create your first Avatar Set",
"Avatar Sets Empty Body": "Avatar Sets bundle a VRChat avatar with multiple channel-point redeems or wardrobe outfits that activate when you switch to that avatar.",
"Avatar Sets Toolbar New": "New Set",
"Avatar Sets Toolbar Search": "Search by name...",
"Avatar Sets Filter All": "All",
"Avatar Sets Filter Active": "Active",
"Avatar Sets Filter Disabled": "Disabled",
"Avatar Sets Filter Live": "Live Now",
"Avatar Sets Filter Master": "Master",
"Avatar Sets Sort Name": "Sort: By Name",
"Avatar Sets Sort Status": "Sort: By Status",
"Avatar Sets Sort Recent": "Sort: Recently Edited",
"Avatar Sets Enable All": "Enable All",
"Avatar Sets Disable All": "Disable All",
"Avatar Sets Delete All": "Delete All",
"Avatar Sets Delete All Confirm": "Delete all avatar sets?",
"Avatar Sets Delete Set": "Delete Set",
"Avatar Sets Delete Set Confirm": "Delete this avatar set?",
"Avatar Sets Card Test": "Test",
"Avatar Sets Card Edit": "Edit",
"Avatar Sets Card Pick Avatar": "Pick Avatar",
"Avatar Sets Card Setup Needed": "Setup Needed",
"Avatar Sets Card Disabled": "Disabled",
"Avatar Sets Card Ready": "Ready",
"Avatar Sets Card Live": "● Live now",
"Avatar Sets Card Waiting": "○ Waiting for this avatar",
"Avatar Sets Card Pick Avatar Hint": "○ Pick an avatar to enable",
"Avatar Sets Card Off": "○ Off",
"Avatar Sets Card Count Redeems Format": "{0} redeems",
"Avatar Sets Card Count Outfits Format": "{0} outfits",
"Avatar Sets Card Count Zero": "0 redeems",
"Avatar Sets Mode Standard": "Standard",
"Avatar Sets Mode Wardrobe": "Wardrobe",
"Avatar Sets Master Badge": "★ Master",
"Avatar Sets Editor Save Close": "Save & Close",
"Avatar Sets Editor Cancel": "Cancel",
"Avatar Sets Manage Button": "Manage Avatar Sets",
"Avatar Sets Summary Format": "{0} sets • {1} active"
```

(Note: that's 40 keys, not 38 - the spec said 38 but the full state matrix in the spec includes 2 additional keys for the editor save/cancel that the table missed. Adding all 40 is correct.)

- [ ] **Step 3: Validate JSON**

Open the file in a JSON validator (or run `ConvertFrom-Json` in PowerShell) to confirm the file is still valid JSON after the edit. Run:

```powershell
Get-Content "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\en-US.json" -Raw | ConvertFrom-Json | Out-Null
Write-Host "OK"
```

Expected: `OK` with no error. If the file is not valid JSON, fix the formatting before proceeding.

- [ ] **Step 4: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded`. The new keys are present in the resource dictionary but not yet referenced by any XAML - that's fine for now. The build will succeed because the keys are unused but valid JSON.

---

## Task 4: Add translations for the 40 keys to every non-English language file

**Files:**
- Modify: Every non-English `*.json` localization file (and matching `.extra.json` files)

This is the largest translation task. The Crystal Relay localization rules (from AGENTS.md) require:
- Informal/friendly register (du/tú/tu)
- Brand and technical terms in English (Bits, Subs, OSC, OSCQuery, VRChat, Twitch, Crystal Relay, StreamElements, Streamlabs, Ko-fi)
- Preserve all format placeholders exactly ({0}, {1}, etc.)
- No empty values
- No accidental English copies unless the key is a brand name

The implementer (a code-generation AI) cannot produce native-quality translations for 40 strings across 8+ languages authentically. The realistic approach:

- For each non-English file, look at how existing similar keys (Universal Triggers keys like "Universal Triggers Card Test", "Universal Triggers Card Edit", "Universal Triggers Manager Title", "Universal Triggers Empty Title", "Universal Triggers Filter All", etc.) are translated in that file.
- Use those translations as the template for the new keys (e.g., "Card Test" should be translated the same way "Universal Triggers Card Test" is translated in that file).
- Where no template exists, provide a best-effort translation following the existing language's register and terminology.
- For placeholder-bearing strings (Format keys), preserve the `{0}`, `{1}`, `{2}` placeholders exactly.
- For emoji-bearing strings (Live, Waiting, Pick Avatar Hint, Off, Master Badge), keep the emoji and translate the surrounding text.
- After this task, the user will review and refine the translations with native speakers.

- [ ] **Step 1: Read every language file**

For each non-English `.json` file (and matching `.extra.json`), read the existing translations of these template keys:

- `Universal Triggers Card Test`
- `Universal Triggers Card Edit`
- `Universal Triggers Manager Title`
- `Universal Triggers Empty Title`
- `Universal Triggers Empty Body`
- `Universal Triggers Filter All`
- `Universal Triggers Filter Active`
- `Universal Triggers Filter Disabled`
- `Universal Triggers Sort Name`
- `Universal Triggers Sort Recent`
- `Universal Triggers Enable All`
- `Universal Triggers Disable All`
- `Universal Triggers Delete All`
- `Universal Triggers Delete Set`
- `Universal Triggers Card Live`
- `Universal Triggers Card Setup Needed`
- `Universal Triggers Source Fooma`

(These are the Universal Triggers equivalents of the new Avatar Sets keys, so they're the closest translation templates.)

- [ ] **Step 2: For each non-English file, add the 40 new keys with best-effort translations**

For each file (de-DE.json + de-DE.extra.json, es-ES.json + es-ES.extra.json, fr-FR.json + fr-FR.extra.json, ja-JP.json + ja-JP.extra.json, ko-KR.json + ko-KR.extra.json, pt-BR.json + pt-BR.extra.json, ru-RU.json + ru-RU.extra.json, zh-CN.json + zh-CN.extra.json, and any others discovered in Task 1):

1. Decide which file (base or extra) is the right home for the new keys. Conventionally, feature additions go in `.extra.json` to keep the base file clean.
2. Add the 40 new keys with translations that follow the existing Universal Triggers templates in that file.
3. Preserve all placeholders exactly.
4. Keep the emoji characters (●, ○, ★) intact in the strings that have them.
5. Keep brand terms (Crystal Relay, Bits, Subs, OSC, VRChat, Twitch) in English.

For the base file vs extra file decision: if both exist for a language, add the new keys to the `.extra.json` file (it's the additions overlay). If only the base file exists, add them there.

Example for `de-DE.extra.json` (informal "du" register, template translations taken from existing `Universal Triggers` keys in that file):
```json
"Avatar Sets Manager Title": "Avatar-Sets",
"Avatar Sets Subtitle Format": "{0} gesamt • {1} aktiv • {2} brauchen Avatar",
"Avatar Sets Empty Title": "Erstelle dein erstes Avatar-Set",
"Avatar Sets Empty Body": "Avatar-Sets bündeln einen VRChat-Avatar mit mehreren Channel-Point-Redeems oder Wardrobe-Outfits, die aktiviert werden, wenn du zu diesem Avatar wechselst.",
"Avatar Sets Toolbar New": "Neues Set",
"Avatar Sets Toolbar Search": "Nach Name suchen...",
"Avatar Sets Filter All": "Alle",
"Avatar Sets Filter Active": "Aktiv",
"Avatar Sets Filter Disabled": "Deaktiviert",
"Avatar Sets Filter Live": "Live jetzt",
"Avatar Sets Filter Master": "Master",
"Avatar Sets Sort Name": "Sortieren: Nach Name",
"Avatar Sets Sort Status": "Sortieren: Nach Status",
"Avatar Sets Sort Recent": "Sortieren: Zuletzt bearbeitet",
"Avatar Sets Enable All": "Alle aktivieren",
"Avatar Sets Disable All": "Alle deaktivieren",
"Avatar Sets Delete All": "Alle löschen",
"Avatar Sets Delete All Confirm": "Alle Avatar-Sets löschen?",
"Avatar Sets Delete Set": "Set löschen",
"Avatar Sets Delete Set Confirm": "Dieses Avatar-Set löschen?",
"Avatar Sets Card Test": "Test",
"Avatar Sets Card Edit": "Bearbeiten",
"Avatar Sets Card Pick Avatar": "Avatar wählen",
"Avatar Sets Card Setup Needed": "Einrichtung nötig",
"Avatar Sets Card Disabled": "Deaktiviert",
"Avatar Sets Card Ready": "Bereit",
"Avatar Sets Card Live": "● Live jetzt",
"Avatar Sets Card Waiting": "○ Warte auf diesen Avatar",
"Avatar Sets Card Pick Avatar Hint": "○ Wähle einen Avatar zum Aktivieren",
"Avatar Sets Card Off": "○ Aus",
"Avatar Sets Card Count Redeems Format": "{0} Redeems",
"Avatar Sets Card Count Outfits Format": "{0} Outfits",
"Avatar Sets Card Count Zero": "0 Redeems",
"Avatar Sets Mode Standard": "Standard",
"Avatar Sets Mode Wardrobe": "Wardrobe",
"Avatar Sets Master Badge": "★ Master",
"Avatar Sets Editor Save Close": "Speichern & Schließen",
"Avatar Sets Editor Cancel": "Abbrechen",
"Avatar Sets Manage Button": "Avatar-Sets verwalten",
"Avatar Sets Summary Format": "{0} Sets • {1} aktiv"
```

Repeat this pattern for each language, adapting to the file's existing terminology and register. Use the existing Universal Triggers translations in that file as the authoritative template.

- [ ] **Step 3: Validate every JSON file**

Run (PowerShell):
```powershell
Get-ChildItem -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization" -Filter "*.json" | ForEach-Object {
    try {
        Get-Content $_.FullName -Raw | ConvertFrom-Json | Out-Null
        Write-Host "OK: $($_.Name)"
    } catch {
        Write-Host "INVALID: $($_.Name) - $_"
    }
}
```

Expected: every file prints `OK: <filename>`. If any print `INVALID`, fix the JSON syntax before continuing.

- [ ] **Step 4: Run the localization audit**

Run:
```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore
```

Expected: the audit reports no missing keys, no empty values, no placeholder mismatches for the new 40 keys. If the audit reports issues, fix them per the audit's guidance.

- [ ] **Step 5: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded`. The new localization keys are now in the resource dictionaries and ready to be referenced from XAML in later tasks.

---

## Task 5: Add the OpenAvatarSetsManagerCommand and AvatarSetSummaryText to MainWindowViewModel

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Find the existing OpenAvatarLibraryManagerCommand or OpenUniversalTriggersManagerCommand**

Search the file for `OpenAvatarLibraryManagerCommand` or `OpenUniversalTriggersManagerCommand`. These existing commands are the pattern to copy. They use the lazy property pattern:

```csharp
public ICommand OpenAvatarLibraryManagerCommand => _openAvatarLibraryManagerCommand ??= new RelayCommand(_ => { ... });
```

- [ ] **Step 2: Add the new command and summary text**

In the section of `MainWindowViewModel.cs` where the other `Open*ManagerCommand` properties live, add two new members. The exact location depends on the file's organization - find the right section by searching for the existing `OpenAvatarLibraryManagerCommand` declaration.

Add this property:

```csharp
public string AvatarSetSummaryText
{
    get
    {
        var total = AvatarRuleProfiles?.Count ?? 0;
        var active = AvatarRuleProfiles?.Count(p => p.IsEnabled) ?? 0;
        return string.Format(LocalizationManager.GetString("Avatar Sets Summary Format"), total, active);
    }
}

private ICommand? _openAvatarSetsManagerCommand;
public ICommand OpenAvatarSetsManagerCommand => _openAvatarSetsManagerCommand ??= new RelayCommand(_ =>
{
    var existing = Application.Current.Windows.OfType<AvatarSetsManagerWindow>().FirstOrDefault();
    if (existing != null)
    {
        existing.Activate();
        return;
    }
    var window = new AvatarSetsManagerWindow
    {
        Owner = Application.Current.MainWindow,
        DataContext = new AvatarSetsManagerViewModel(this, App.AvatarImageService)
    };
    window.ShowDialog();
});
```

Add the following `using` statements at the top of the file if not already present (search for them first to avoid duplicates):
- `using System.Linq;` (for `OfType<>`, `FirstOrDefault`, `Count`)
- `using System.Windows;` (for `Application.Current`)
- `using VrcTwitchOscBridge.Services;` or wherever `LocalizationManager` lives (search for existing usage of `LocalizationManager.GetString` to find the right using)

- [ ] **Step 3: Trigger AvatarSetSummaryText to refresh when AvatarRuleProfiles changes**

The summary text is computed from `AvatarRuleProfiles`, so it needs to refresh when the collection changes. Find the place in `MainWindowViewModel` where `AvatarRuleProfiles` is initialized (around line 1084 per the research) and ensure that:

1. The collection is set up with property change notifications (it's an `ObservableCollection<AvatarTriggerProfile>`, which already has `CollectionChanged`).
2. Add a `CollectionChanged` handler that raises `OnPropertyChanged(nameof(AvatarSetSummaryText))` (or `OnPropertyChanged("AvatarSetSummaryText")` depending on the project's MVVM helper style).

Find the constructor of `MainWindowViewModel` and add this near the end (before any other initialization that might trigger it):

```csharp
if (AvatarRuleProfiles != null)
{
    AvatarRuleProfiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AvatarSetSummaryText));
    foreach (var profile in AvatarRuleProfiles)
    {
        profile.PropertyChanged += (_, _) => OnPropertyChanged(nameof(AvatarSetSummaryText));
    }
}
```

If the existing code already has a similar handler for `AvatarRuleProfiles.CollectionChanged`, extend it instead of adding a duplicate.

- [ ] **Step 4: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded` (with possibly a warning about `AvatarSetsManagerWindow` and `AvatarSetsManagerViewModel` not existing yet - that's fine, the warning is expected for this task and will resolve in Task 7 when those classes are created). If the build has ERRORS, the most common cause is missing `using` statements or a typo in the type name.

If the build fails because `AvatarSetsManagerWindow` and `AvatarSetsManagerViewModel` don't exist yet, that's expected. To avoid the build error and still verify the rest of the code, temporarily wrap the `new AvatarSetsManagerWindow` and `new AvatarSetsManagerViewModel` lines in `#if false ... #endif` and add a TODO comment. Remove the `#if false` block in Task 13 when those classes exist.

Alternative: skip the verification build for this task, do the inline UI removal in Task 6, then build after both changes.

---

## Task 6: Remove the inline Avatar Set list + editor from MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

This is the demolition task. The inline list pane and editor pane are removed and replaced with a small "Manage Avatar Sets" button + summary text.

- [ ] **Step 1: Find the inline Avatar Set list pane**

Open `MainWindow.xaml`. The list pane is the `<ListBox ItemsSource="{Binding AvatarRuleProfiles}" ...>` block around line 4364. The exact line may have shifted; search for `AvatarRuleProfiles` to locate it.

The block to remove includes:
- The `<ListBox>` itself (and any wrapping container that's specific to this list)
- The `DataTemplate DataType="{x:Type models:AvatarTriggerProfile}"` at lines 1425-1537 (the per-card template for the list)
- The empty state `TextBlock` reading "Add an avatar set to get started." around lines 4652-4707

Verify by searching for these strings:
- `AvatarRuleProfiles` - find the list binding
- `Add an avatar set to get started` - find the empty state
- The card data template at the top of the file

- [ ] **Step 2: Remove the list pane and its data template**

Delete the following XAML blocks:
1. The `<ListBox ItemsSource="{Binding AvatarRuleProfiles}" ...>` block (entire element, including its wrapping container if specific to the avatar sets section)
2. The `<DataTemplate DataType="{x:Type models:AvatarTriggerProfile}">` block at lines 1425-1537 in `Window.Resources`
3. The empty state `<TextBlock>` reading "Add an avatar set to get started." with its surrounding visibility logic

Be careful not to remove any XAML that's used by the editor pane (Task 6 step 3) or by other Crystal Relay systems. The AvatarTriggerProfile DataTemplate is also used by the editor pane - DO NOT delete it if it's referenced by the editor.

Verify by searching for other `ContentControl Content="{Binding SelectedAvatarProfile}"` or similar bindings - if the editor still references the same DataTemplate, leave the DataTemplate in place and let Task 9 move it to the new window.

- [ ] **Step 3: Remove the inline editor pane**

Find the editor pane - it's the second `<DataTemplate DataType="{x:Type models:AvatarTriggerProfile}">` at lines 5419-5830, hosted by `<ContentControl Content="{Binding SelectedAvatarProfile}">` around line 5384. This is the full editor (name textbox, avatar picker, set-trigger master reward, channel point rules, wardrobe editor).

Delete:
1. The `<ContentControl Content="{Binding SelectedAvatarProfile}">` block (and the Grid container that hosts it, if specific to the editor)
2. The entire second `DataTemplate DataType="{x:Type models:AvatarTriggerProfile}"` block

This will be moved to the new manager window in Task 9.

- [ ] **Step 4: Add the Manage Avatar Sets button + summary text**

In the same XAML location where the list pane was (so the layout doesn't shift dramatically), add a small inline summary block:

```xaml
<StackPanel Orientation="Vertical" Margin="0,0,0,8">
    <TextBlock Text="{Binding AvatarSetSummaryText}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,0,0,4" />
    <Button Content="{loc:Translate 'Avatar Sets Manage Button'}" 
            Command="{Binding OpenAvatarSetsManagerCommand}"
            Style="{StaticResource SecondaryButtonStyle}"
            HorizontalAlignment="Left" />
</StackPanel>
```

The `loc:Translate` markup extension and `SecondaryButtonStyle` are the existing patterns in the file - use the same style as the other "Manage" buttons (e.g., the Universal Triggers "Manage Triggers" button, the Avatar Library "Manage Library" button).

If the surrounding XAML uses a Grid with rows for the list/editor layout, replace the two rows (list row + editor row) with a single row containing the new `StackPanel`.

- [ ] **Step 5: Find and remove unused visibility flags**

Search `MainWindow.xaml` for these bindings (they may be in Visibility converters or DataTriggers):
- `{Binding IsViewingAvatarTriggers`
- `{Binding IsViewingMasterAvatar`
- `{Binding SelectedAvatarSetupTitle`
- `{Binding SelectedAvatarProfileStatusText`

For each binding still in `MainWindow.xaml`, remove the XAML block that uses it. If a binding is also used elsewhere (e.g., by another Crystal Relay section), leave it.

- [ ] **Step 6: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded` with potentially warnings about `OpenAvatarSetsManagerCommand`, `AvatarSetSummaryText`, `AvatarSetsManagerWindow`, and `AvatarSetsManagerViewModel` not resolving - those will resolve in Tasks 7 and 8. The XAML parsing should succeed.

If the build has XAML errors, the most common cause is an unclosed tag from a partial deletion. Check the file's last 50 lines and search for orphaned `</Grid>` or `</DataTemplate>` tags.

---

## Task 7: Create the AvatarSetCardViewModel skeleton

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/AvatarSetCardViewModel.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add new file to `<Compile>`)

- [ ] **Step 1: Create the skeleton file**

Create `VrcTwitchOscBridge/ViewModels/AvatarSetCardViewModel.cs` with the skeleton below. This is a compilable stub - the full implementation comes in Task 8.

```csharp
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed partial class AvatarSetCardViewModel : ObservableObject, IDisposable
{
    private readonly AvatarTriggerProfile _profile;
    private readonly AvatarImageService _imageService;
    private readonly Func<MainWindowViewModel> _mainVmAccessor;
    private CancellationTokenSource? _imageLoadCts;
    private string? _thumbnailUrl;

    public AvatarSetCardViewModel(
        AvatarTriggerProfile profile,
        AvatarImageService imageService,
        Func<MainWindowViewModel> mainVmAccessor)
    {
        _profile = profile;
        _imageService = imageService;
        _mainVmAccessor = mainVmAccessor;
        _profile.PropertyChanged += OnProfilePropertyChanged;
    }

    public AvatarTriggerProfile Profile => _profile;

    public string DisplayTitle => string.IsNullOrWhiteSpace(_profile.Name) ? "New Set" : _profile.Name;
    public string AvatarSubtitle => !string.IsNullOrWhiteSpace(_profile.AvatarName)
        ? _profile.AvatarName
        : !string.IsNullOrWhiteSpace(_profile.AvatarId)
            ? _profile.AvatarId
            : "(no avatar picked)";

    public bool IsEnabled => _profile.IsEnabled;
    public bool IsMaster => _profile.IsMasterProfile;
    public bool IsLive => _profile.IsCurrentAvatarActive;
    public bool HasAvatar => !string.IsNullOrWhiteSpace(_profile.AvatarId);
    public bool IsWardrobeMode => _profile.UseWardrobeMode;

    [ObservableProperty]
    private ImageSource? _image;

    [ObservableProperty]
    private bool _isTestDisabled;

    public ICommand OpenEditorCommand { get; }
    public ICommand TestCommand { get; }

    public void SetThumbnailUrl(string? url)
    {
        _thumbnailUrl = url;
        _ = LoadImageAsync();
    }

    private async Task LoadImageAsync()
    {
        _imageLoadCts?.Cancel();
        _imageLoadCts = new CancellationTokenSource();
        var ct = _imageLoadCts.Token;
        if (!HasAvatar)
        {
            Image = _imageService.GetPlaceholderImage();
            return;
        }
        try
        {
            var img = await _imageService.GetAvatarImageAsync(_profile.AvatarId, null, _thumbnailUrl, ct);
            if (!ct.IsCancellationRequested && img != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => Image = img);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(AvatarSubtitle));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsMaster));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(IsWardrobeMode));
        if (e.PropertyName == nameof(AvatarTriggerProfile.AvatarId))
        {
            _ = LoadImageAsync();
        }
    }

    public void Dispose()
    {
        _imageLoadCts?.Cancel();
        _imageLoadCts?.Dispose();
        _profile.PropertyChanged -= OnProfilePropertyChanged;
    }
}
```

(Verify the existing codebase uses `CommunityToolkit.Mvvm.ComponentModel.ObservableObject` and `[ObservableProperty]` - search for any existing VM file like `UniversalTriggersManagerViewModel.cs` to confirm the imports. If the codebase uses a different MVVM helper, match the existing pattern.)

- [ ] **Step 2: Add the new file to the csproj**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`. Find the `<ItemGroup>` with other `<Compile Include="ViewModels\...">` entries. Add:

```xml
<Compile Include="ViewModels\AvatarSetCardViewModel.cs" />
```

- [ ] **Step 3: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded`. The card VM skeleton compiles. The placeholder image is set on init for cards without avatars, and the async load path is set up for cards with avatars. The full card VM (with status pills, mode pills, brushes) will be added in Task 8.

---

## Task 8: Flesh out the AvatarSetCardViewModel with status pills, mode pill, and brushes

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSetCardViewModel.cs`

- [ ] **Step 1: Add the new derived properties**

Replace the file with the full implementation:

```csharp
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed partial class AvatarSetCardViewModel : ObservableObject, IDisposable
{
    private readonly AvatarTriggerProfile _profile;
    private readonly AvatarImageService _imageService;
    private readonly Func<MainWindowViewModel> _mainVmAccessor;
    private CancellationTokenSource? _imageLoadCts;
    private string? _thumbnailUrl;

    public AvatarSetCardViewModel(
        AvatarTriggerProfile profile,
        AvatarImageService imageService,
        Func<MainWindowViewModel> mainVmAccessor)
    {
        _profile = profile;
        _imageService = imageService;
        _mainVmAccessor = mainVmAccessor;
        _profile.PropertyChanged += OnProfilePropertyChanged;
        // OpenEditorCommand and TestCommand are set by AvatarSetsManagerViewModel
        // when the card is created (see Task 10, RebuildCards). The card VM
        // doesn't create them itself because the editor overlay lives on the
        // manager VM, not on MainWindowViewModel.
    }

    public AvatarTriggerProfile Profile => _profile;

    public string DisplayTitle => string.IsNullOrWhiteSpace(_profile.Name) ? "New Set" : _profile.Name;
    public string AvatarSubtitle => !string.IsNullOrWhiteSpace(_profile.AvatarName)
        ? _profile.AvatarName
        : !string.IsNullOrWhiteSpace(_profile.AvatarId)
            ? _profile.AvatarId
            : "(no avatar picked)";

    public bool IsEnabled => _profile.IsEnabled;
    public bool IsMaster => _profile.IsMasterProfile;
    public bool IsLive => _profile.IsCurrentAvatarActive;
    public bool HasAvatar => !string.IsNullOrWhiteSpace(_profile.AvatarId);
    public bool IsWardrobeMode => _profile.UseWardrobeMode;
    public bool IsDisabled => !_profile.IsEnabled;

    public int RedeemCount => _profile.ChannelPointRules?.Count ?? 0;
    public int OutfitCount => _profile.WardrobeOutfits?.Count ?? 0;
    public bool HasAnyRules => IsWardrobeMode ? OutfitCount > 0 : RedeemCount > 0;

    public string CountPillText
    {
        get
        {
            if (IsWardrobeMode)
            {
                return OutfitCount == 1
                    ? LocalizationManager.GetString("Avatar Sets Card Count Outfits Format").Replace("{0}", "1")
                    : string.Format(LocalizationManager.GetString("Avatar Sets Card Count Outfits Format"), OutfitCount);
            }
            return RedeemCount == 0
                ? LocalizationManager.GetString("Avatar Sets Card Count Zero")
                : string.Format(LocalizationManager.GetString("Avatar Sets Card Count Redeems Format"), RedeemCount);
        }
    }

    public string ModePillText
    {
        get
        {
            if (!HasAnyRules) return string.Empty;
            return IsWardrobeMode
                ? LocalizationManager.GetString("Avatar Sets Mode Wardrobe")
                : LocalizationManager.GetString("Avatar Sets Mode Standard");
        }
    }

    public string StatusPillText
    {
        get
        {
            if (!HasAvatar) return LocalizationManager.GetString("Avatar Sets Card Setup Needed");
            if (IsDisabled) return LocalizationManager.GetString("Avatar Sets Card Disabled");
            return LocalizationManager.GetString("Avatar Sets Card Ready");
        }
    }

    public string LiveText
    {
        get
        {
            if (!HasAvatar) return LocalizationManager.GetString("Avatar Sets Card Pick Avatar Hint");
            if (IsDisabled) return LocalizationManager.GetString("Avatar Sets Card Off");
            if (IsLive) return LocalizationManager.GetString("Avatar Sets Card Live");
            return LocalizationManager.GetString("Avatar Sets Card Waiting");
        }
    }

    public Brush StatusStripeBrush
    {
        get
        {
            if (!HasAvatar) return ResolveBrush("StatusStripeWarnBrush", System.Windows.Media.Brushes.Goldenrod);
            if (IsDisabled) return ResolveBrush("StatusStripeOffBrush", System.Windows.Media.Brushes.Gray);
            return ResolveBrush("StatusStripeReadyBrush", System.Windows.Media.Brushes.LimeGreen);
        }
    }

    public Brush ModePillBrush => IsWardrobeMode
        ? new SolidColorBrush(Color.FromRgb(0xEC, 0x48, 0x99))
        : new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));

    public Brush LiveTextBrush
    {
        get
        {
            if (!HasAvatar) return new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            if (IsLive) return new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
            if (IsDisabled) return new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            return new SolidColorBrush(Color.FromRgb(0xB8, 0xA8, 0xD4));
        }
    }

    public bool CanTest => HasAvatar && IsWardrobeMode && OutfitCount > 0 && !IsDisabled;
    public bool IsTestDisabled => !CanTest;

    [ObservableProperty]
    private ImageSource? _image;

    public ICommand? OpenEditorCommand { get; set; }
    public ICommand? TestCommand { get; set; }

    public void SetThumbnailUrl(string? url)
    {
        _thumbnailUrl = url;
        _ = LoadImageAsync();
    }

    private async Task LoadImageAsync()
    {
        _imageLoadCts?.Cancel();
        _imageLoadCts = new CancellationTokenSource();
        var ct = _imageLoadCts.Token;
        if (!HasAvatar)
        {
            Image = _imageService.GetPlaceholderImage();
            return;
        }
        try
        {
            var img = await _imageService.GetAvatarImageAsync(_profile.AvatarId, null, _thumbnailUrl, ct);
            if (!ct.IsCancellationRequested && img != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => Image = img);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static Brush ResolveBrush(string name, Brush fallback)
    {
        try
        {
            if (Application.Current?.Resources[name] is Brush b) return b;
        }
        catch { }
        return fallback;
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(AvatarSubtitle));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(IsMaster));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(IsWardrobeMode));
        OnPropertyChanged(nameof(RedeemCount));
        OnPropertyChanged(nameof(OutfitCount));
        OnPropertyChanged(nameof(HasAnyRules));
        OnPropertyChanged(nameof(CountPillText));
        OnPropertyChanged(nameof(ModePillText));
        OnPropertyChanged(nameof(StatusPillText));
        OnPropertyChanged(nameof(LiveText));
        OnPropertyChanged(nameof(StatusStripeBrush));
        OnPropertyChanged(nameof(ModePillBrush));
        OnPropertyChanged(nameof(LiveTextBrush));
        OnPropertyChanged(nameof(CanTest));
        OnPropertyChanged(nameof(IsTestDisabled));
        if (e.PropertyName == nameof(AvatarTriggerProfile.AvatarId))
        {
            _ = LoadImageAsync();
        }
    }

    public void Dispose()
    {
        _imageLoadCts?.Cancel();
        _imageLoadCts?.Dispose();
        _profile.PropertyChanged -= OnProfilePropertyChanged;
    }
}
```

- [ ] **Step 2: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded` with errors about `TestAvatarSet` not existing on `MainWindowViewModel` - that's expected. We will add it in Task 11.

If the build fails with errors about `LocalizationManager` namespace, search the existing codebase for the correct using statement (it's likely `VrcTwitchOscBridge.Services` or `VrcTwitchOscBridge.Resources.Localization`).

---

## Task 9: Create the AvatarSetsManagerViewModel skeleton

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add new file to `<Compile>`)

- [ ] **Step 1: Create the skeleton file**

Create `VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs` with the skeleton below. The full implementation comes in Task 10.

```csharp
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed partial class AvatarSetsManagerViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel _mainVm;
    private readonly AvatarImageService _imageService;
    private readonly ObservableCollection<AvatarSetCardViewModel> _cardsBacking = new();

    public AvatarSetsManagerViewModel(MainWindowViewModel mainVm, AvatarImageService imageService)
    {
        _mainVm = mainVm;
        _imageService = imageService;
        Cards = new ListCollectionView(_cardsBacking);
        _mainVm.AvatarRuleProfiles.CollectionChanged += OnProfileCollectionChanged;
        RebuildCards();
    }

    public ICollectionView Cards { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AvatarSetsFilterMode _filterMode = AvatarSetsFilterMode.All;

    [ObservableProperty]
    private AvatarSetsSortMode _sortMode = AvatarSetsSortMode.ByName;

    [ObservableProperty]
    private AvatarTriggerProfile? _selectedProfile;

    [ObservableProperty]
    private bool _isEditorOpen;

    public string SubtitleSummary => string.Format(
        LocalizationManager.GetString("Avatar Sets Subtitle Format"),
        _cardsBacking.Count,
        _cardsBacking.Count(c => c.IsEnabled),
        _cardsBacking.Count(c => !c.HasAvatar));

    public int CountAll => _cardsBacking.Count;
    public int CountActive => _cardsBacking.Count(c => c.IsEnabled);
    public int CountDisabled => _cardsBacking.Count(c => c.IsDisabled);
    public int CountLiveNow => _cardsBacking.Count(c => c.IsLive);
    public int CountMaster => _cardsBacking.Count(c => c.IsMaster);

    private void OnProfileCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildCards();

    private void RebuildCards()
    {
        // Implementation in Task 10
    }

    public void Dispose()
    {
        _mainVm.AvatarRuleProfiles.CollectionChanged -= OnProfileCollectionChanged;
        foreach (var card in _cardsBacking) card.Dispose();
        _cardsBacking.Clear();
    }
}
```

- [ ] **Step 2: Add the new file to the csproj**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`. Find the `<ItemGroup>` with other `<Compile Include="ViewModels\...">` entries. Add:

```xml
<Compile Include="ViewModels\AvatarSetsManagerViewModel.cs" />
```

- [ ] **Step 3: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded`. The manager VM skeleton compiles.

---

## Task 10: Flesh out the AvatarSetsManagerViewModel with full filter, sort, search, and commands

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs`

- [ ] **Step 1: Replace the skeleton with the full implementation**

Replace the file with:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed partial class AvatarSetsManagerViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel _mainVm;
    private readonly AvatarImageService _imageService;
    private readonly ObservableCollection<AvatarSetCardViewModel> _cardsBacking = new();

    public AvatarSetsManagerViewModel(MainWindowViewModel mainVm, AvatarImageService imageService)
    {
        _mainVm = mainVm;
        _imageService = imageService;
        Cards = new ListCollectionView(_cardsBacking);
        _mainVm.AvatarRuleProfiles.CollectionChanged += OnProfileCollectionChanged;
        RebuildCards();
        RefreshThumbnailUrls();
    }

    public ICollectionView Cards { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilterSort();

    [ObservableProperty]
    private AvatarSetsFilterMode _filterMode = AvatarSetsFilterMode.All;

    partial void OnFilterModeChanged(AvatarSetsFilterMode value)
    {
        ApplyFilterSort();
        OnPropertyChanged(nameof(CountAll));
        OnPropertyChanged(nameof(CountActive));
        OnPropertyChanged(nameof(CountDisabled));
        OnPropertyChanged(nameof(CountLiveNow));
        OnPropertyChanged(nameof(CountMaster));
    }

    [ObservableProperty]
    private AvatarSetsSortMode _sortMode = AvatarSetsSortMode.ByName;

    partial void OnSortModeChanged(AvatarSetsSortMode value) => ApplyFilterSort();

    [ObservableProperty]
    private AvatarTriggerProfile? _selectedProfile;

    [ObservableProperty]
    private bool _isEditorOpen;

    public string SubtitleSummary
    {
        get
        {
            int total = _cardsBacking.Count;
            int active = _cardsBacking.Count(c => c.IsEnabled);
            int needAvatar = _cardsBacking.Count(c => !c.HasAvatar);
            return string.Format(LocalizationManager.GetString("Avatar Sets Subtitle Format"), total, active, needAvatar);
        }
    }

    public int CountAll => _cardsBacking.Count;
    public int CountActive => _cardsBacking.Count(c => c.IsEnabled);
    public int CountDisabled => _cardsBacking.Count(c => c.IsDisabled);
    public int CountLiveNow => _cardsBacking.Count(c => c.IsLive);
    public int CountMaster => _cardsBacking.Count(c => c.IsMaster);

    public IRelayCommand AddNewSetCommand { get; }
    public IRelayCommand OpenEditorCommand { get; }
    public IRelayCommand CloseEditorCommand { get; }
    public IRelayCommand EnableAllCommand { get; }
    public IRelayCommand DisableAllCommand { get; }
    public IRelayCommand DeleteAllCommand { get; }
    public IRelayCommand ShowAllCommand { get; }
    public IRelayCommand ShowActiveCommand { get; }
    public IRelayCommand ShowDisabledCommand { get; }
    public IRelayCommand ShowLiveNowCommand { get; }
    public IRelayCommand ShowMasterCommand { get; }
    public IRelayCommand SortByNameCommand { get; }
    public IRelayCommand SortByStatusCommand { get; }
    public IRelayCommand SortByRecentCommand { get; }
    public IRelayCommand DeleteSetCommand { get; }

    public AvatarSetsManagerViewModel(MainWindowViewModel mainVm, AvatarImageService imageService, bool dummy = false) : this(mainVm, imageService) { }

    private void InitializeCommands()
    {
        AddNewSetCommand = new RelayCommand(() =>
        {
            _mainVm.AddAvatarProfileCommand.Execute(null);
            var last = _mainVm.AvatarRuleProfiles.LastOrDefault();
            if (last != null)
            {
                SelectedProfile = last;
                IsEditorOpen = true;
            }
        });
        OpenEditorCommand = new RelayCommand<AvatarTriggerProfile>(p =>
        {
            if (p == null) return;
            SelectedProfile = p;
            IsEditorOpen = true;
        });
        CloseEditorCommand = new RelayCommand(() =>
        {
            IsEditorOpen = false;
            SelectedProfile = null;
        });
        EnableAllCommand = new RelayCommand(() => SetAllEnabled(true));
        DisableAllCommand = new RelayCommand(() => SetAllEnabled(false));
        DeleteAllCommand = new RelayCommand(() =>
        {
            var result = MessageBox.Show(
                LocalizationManager.GetString("Avatar Sets Delete All Confirm"),
                "Crystal Relay",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _mainVm.DeleteAllAvatarProfilesCommand.Execute(null);
            }
        });
        ShowAllCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.All);
        ShowActiveCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.Active);
        ShowDisabledCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.Disabled);
        ShowLiveNowCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.LiveNow);
        ShowMasterCommand = new RelayCommand(() => FilterMode = AvatarSetsFilterMode.Master);
        SortByNameCommand = new RelayCommand(() => SortMode = AvatarSetsSortMode.ByName);
        SortByStatusCommand = new RelayCommand(() => SortMode = AvatarSetsSortMode.ByStatus);
        SortByRecentCommand = new RelayCommand(() => SortMode = AvatarSetsSortMode.RecentlyEdited);
        DeleteSetCommand = new RelayCommand(() =>
        {
            if (SelectedProfile == null) return;
            var result = MessageBox.Show(
                LocalizationManager.GetString("Avatar Sets Delete Set Confirm"),
                "Crystal Relay",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _mainVm.SelectedAvatarProfile = SelectedProfile;
                _mainVm.DeleteSelectedAvatarProfileCommand.Execute(null);
                IsEditorOpen = false;
                SelectedProfile = null;
            }
        });
    }

    private void SetAllEnabled(bool enabled)
    {
        foreach (var card in _cardsBacking)
        {
            card.Profile.IsEnabled = enabled;
        }
        OnPropertyChanged(nameof(SubtitleSummary));
        ApplyFilterSort();
    }

    private async void RefreshThumbnailUrls()
    {
        // Fetch the current avatar list snapshot to get thumbnail URLs for cards.
        // This is best-effort - if VRChat is offline, cards show the placeholder.
        try
        {
            var cookie = _mainVm.GetType().GetProperty("VrChatAuthCookie")?.GetValue(_mainVm) as string;
            if (string.IsNullOrWhiteSpace(cookie)) return;
            var apiClient = ServiceLocator.Get<VrChatApiClient>();
            if (apiClient == null) return;
            var avatars = await apiClient.GetSelectableAvatarsAsync(cookie, null, CancellationToken.None);
            var urlMap = avatars.ToDictionary(a => a.Id, a => a.ThumbnailUrl);
            foreach (var card in _cardsBacking)
            {
                if (card.Profile.AvatarId != null && urlMap.TryGetValue(card.Profile.AvatarId, out var url))
                {
                    card.SetThumbnailUrl(url);
                }
            }
        }
        catch { /* best-effort */ }
    }

    private void OnProfileCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildCards();

    private void RebuildCards()
    {
        foreach (var card in _cardsBacking) card.Dispose();
        _cardsBacking.Clear();
        foreach (var profile in _mainVm.AvatarRuleProfiles)
        {
            var card = new AvatarSetCardViewModel(profile, _imageService, () => _mainVm);
            card.OpenEditorCommand = new RelayCommand(() =>
            {
                SelectedProfile = profile;
                IsEditorOpen = true;
            });
            card.TestCommand = new RelayCommand(() => _mainVm.TestAvatarSet(profile), () => card.CanTest);
            _cardsBacking.Add(card);
        }
        ApplyFilterSort();
        OnPropertyChanged(nameof(SubtitleSummary));
        OnPropertyChanged(nameof(CountAll));
        OnPropertyChanged(nameof(CountActive));
        OnPropertyChanged(nameof(CountDisabled));
        OnPropertyChanged(nameof(CountLiveNow));
        OnPropertyChanged(nameof(CountMaster));
        RefreshThumbnailUrls();
    }

    private void ApplyFilterSort()
    {
        if (Cards is not ListCollectionView view) return;
        view.Filter = obj =>
        {
            if (obj is not AvatarSetCardViewModel card) return false;
            if (!MatchesFilter(card)) return false;
            if (!MatchesSearch(card)) return false;
            return true;
        };
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(
            SortMode == AvatarSetsSortMode.ByName ? nameof(AvatarSetCardViewModel.DisplayTitle) :
            SortMode == AvatarSetsSortMode.RecentlyEdited ? nameof(AvatarSetCardViewModel.Profile) :
            nameof(AvatarSetCardViewModel.IsLive),
            SortMode == AvatarSetsSortMode.ByName ? ListSortDirection.Ascending : ListSortDirection.Descending));
    }

    private bool MatchesFilter(AvatarSetCardViewModel card) => FilterMode switch
    {
        AvatarSetsFilterMode.Active => card.IsEnabled,
        AvatarSetsFilterMode.Disabled => card.IsDisabled,
        AvatarSetsFilterMode.LiveNow => card.IsLive,
        AvatarSetsFilterMode.Master => card.IsMaster,
        _ => true
    };

    private bool MatchesSearch(AvatarSetCardViewModel card)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var s = SearchText.Trim();
        return (card.DisplayTitle?.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (card.AvatarSubtitle?.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public void Dispose()
    {
        _mainVm.AvatarRuleProfiles.CollectionChanged -= OnProfileCollectionChanged;
        foreach (var card in _cardsBacking) card.Dispose();
        _cardsBacking.Clear();
    }
}
```

(Note: there's a small design issue here - the `InitializeCommands` method is private but the command properties are `IRelayCommand` initialized as fields. To match the existing codebase pattern, the implementer should adjust this to use the lazy property pattern OR initialize all commands in the constructor. The pattern in `UniversalTriggersManagerViewModel.cs` is the reference - check it and match.)

- [ ] **Step 2: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded` with possibly warnings about `ServiceLocator` not existing - that helper may or may not exist in this codebase. If it doesn't, replace `ServiceLocator.Get<VrChatApiClient>()` with how `VrChatApiClient` is actually obtained. Search for existing usages of `VrChatApiClient` to find the right pattern.

---

## Task 11: Add the TestAvatarSet method to MainWindowViewModel

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Find the right place to add the method**

Search for the existing `OpenAvatarPickerCommand` definition in `MainWindowViewModel.cs` (around line 6380-6441 per the research). The new method should go in the same section, since it relates to avatar set editor actions.

- [ ] **Step 2: Add the helper method**

Add this public method (matching the access modifier style of the surrounding code - if the existing methods are `public`, use `public`):

```csharp
public void TestAvatarSet(AvatarTriggerProfile profile)
{
    if (profile == null) return;
    if (!profile.UseWardrobeMode) return;
    var firstOutfit = profile.WardrobeOutfits?.FirstOrDefault(o => o.IsEnabled);
    if (firstOutfit == null) return;
    SelectedWardrobeOutfit = firstOutfit;
    TestWardrobeOutfitCommand.Execute(null);
}
```

- [ ] **Step 3: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded`. The helper method exists and is called by the card VMs (set up in Task 10, RebuildCards).

---

## Task 12: Create the AvatarSetsManagerWindow XAML + code-behind (skeleton with title bar + empty state)

**Files:**
- Create: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`
- Create: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add new XAML + cs to `<Page>` and `<Compile>`)

- [ ] **Step 1: Create the code-behind**

Create `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs` with:

```csharp
using System.Windows;
using System.Windows.Input;

namespace VrcTwitchOscBridge;

public partial class AvatarSetsManagerWindow : Window
{
    public AvatarSetsManagerWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
```

- [ ] **Step 2: Create the XAML skeleton**

Create `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` with the skeleton below. The card grid, toolbar, and editor overlay will be added in Tasks 13-15.

```xml
<Window x:Class="VrcTwitchOscBridge.AvatarSetsManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Resources.Localization"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:m="clr-namespace:VrcTwitchOscBridge.Models"
        Title="Avatar Sets"
        Width="1100" Height="700"
        MinWidth="800" MinHeight="500"
        WindowStartupLocation="CenterOwner"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        shell:WindowChrome.IsHitTestVisibleInChrome="True">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0" CornerRadius="8" GlassFrameThickness="0" />
    </shell:WindowChrome.WindowChrome>

    <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="1" CornerRadius="8">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>

            <!-- Title bar -->
            <Border Grid.Row="0" Background="{DynamicResource PanelHeaderBrush}" MouseLeftButtonDown="OnTitleBarMouseDown">
                <Grid Margin="16,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="🎯" FontSize="18" VerticalAlignment="Center" />
                    <StackPanel Grid.Column="1" Margin="12,0,0,0">
                        <TextBlock Text="{loc:Translate 'Avatar Sets Manager Title'}" FontWeight="Bold" FontSize="14" Foreground="{DynamicResource TextBrush}" />
                        <TextBlock Text="{Binding SubtitleSummary}" FontSize="11" Foreground="{DynamicResource MutedBrush}" />
                    </StackPanel>
                    <Button Grid.Column="2" Content="✕" Width="32" Height="24" Click="OnCloseClicked" Style="{StaticResource SecondaryButtonStyle}" />
                </Grid>
            </Border>

            <!-- Content area (toolbar + cards + empty state will be added in Tasks 13-15) -->
            <Grid Grid.Row="1">
                <TextBlock Text="Content goes here" VerticalAlignment="Center" HorizontalAlignment="Center" Foreground="{DynamicResource MutedBrush}" />
            </Grid>
        </Grid>
    </Border>
</Window>
```

(Verify the namespace `VrcTwitchOscBridge.Resources.Localization` is correct for the `loc:Translate` markup extension - if it's different in this codebase, adjust. Also verify the brush names `PanelBrush`, `PanelHeaderBrush`, `AccentBrush`, `TextBrush`, `MutedBrush` are the actual resource keys - search the existing `App.xaml` or `UniversalTriggersManagerWindow.xaml` to confirm.)

- [ ] **Step 3: Add the new files to the csproj**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`. Find the `<ItemGroup>` with other `<Page Include="...">` and `<Compile Include="...">` entries (search for `MainWindow.xaml` to locate the right group). Add:

```xml
<Page Include="AvatarSetsManagerWindow.xaml">
  <Generator>MSBuild:Compile</Generator>
  <SubType>Designer</SubType>
</Page>
<Compile Include="AvatarSetsManagerWindow.xaml.cs" />
```

Match the exact `Generator` and `SubType` values used by other Page entries in the same group.

- [ ] **Step 4: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded`. The window skeleton compiles. The "Content goes here" placeholder is visible when the window opens (it won't open yet from the UI because the command in MainWindowViewModel is gated on the type existing - now that the type exists, the user could click the Manage button and see the window).

---

## Task 13: Add the toolbar (New, Search, Filter, Sort, Enable/Disable All) to the window

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`

- [ ] **Step 1: Replace the "Content goes here" TextBlock with the toolbar + card grid placeholder**

In `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`, replace the content area's inner Grid with:

```xml
<Grid Grid.Row="1">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <!-- Toolbar -->
    <Border Grid.Row="0" Background="{DynamicResource PanelSubBrush}" Padding="16,10" BorderBrush="{DynamicResource DividerBrush}" BorderThickness="0,0,0,1">
        <StackPanel Orientation="Horizontal">
            <Button Content="{loc:Translate 'Avatar Sets Toolbar New'}" 
                    Command="{Binding AddNewSetCommand}"
                    Style="{StaticResource AccentButtonStyle}" 
                    Padding="12,6" Margin="0,0,8,0" />
            <Grid Width="240" Margin="0,0,8,0">
                <TextBox x:Name="SearchBox" Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" 
                         Background="{DynamicResource InputBrush}" Foreground="{DynamicResource TextBrush}"
                         BorderBrush="{DynamicResource DividerBrush}" Padding="6,4" />
                <TextBlock Text="{loc:Translate 'Avatar Sets Toolbar Search'}" 
                           Foreground="{DynamicResource MutedBrush}" 
                           IsHitTestVisible="False" Margin="8,5,0,0"
                           Visibility="{Binding Text.IsEmpty, ElementName=SearchBox, Converter={StaticResource BoolToVisibilityConverter}}" />
            </Grid>
            <ToggleButton Content="{loc:Translate 'Avatar Sets Filter All'}" IsChecked="{Binding FilterMode, Converter={StaticResource EnumEqualsConverter}, ConverterParameter=All}" 
                          Style="{StaticResource FilterChipStyle}" Margin="0,0,4,0" />
            <ToggleButton Content="{loc:Translate 'Avatar Sets Filter Active'}" IsChecked="{Binding FilterMode, Converter={StaticResource EnumEqualsConverter}, ConverterParameter=Active}" 
                          Style="{StaticResource FilterChipStyle}" Margin="0,0,4,0" />
            <ToggleButton Content="{loc:Translate 'Avatar Sets Filter Disabled'}" IsChecked="{Binding FilterMode, Converter={StaticResource EnumEqualsConverter}, ConverterParameter=Disabled}" 
                          Style="{StaticResource FilterChipStyle}" Margin="0,0,4,0" />
            <ToggleButton Content="{loc:Translate 'Avatar Sets Filter Live'}" IsChecked="{Binding FilterMode, Converter={StaticResource EnumEqualsConverter}, ConverterParameter=LiveNow}" 
                          Style="{StaticResource FilterChipStyle}" Margin="0,0,4,0" />
            <ToggleButton Content="{loc:Translate 'Avatar Sets Filter Master'}" IsChecked="{Binding FilterMode, Converter={StaticResource EnumEqualsConverter}, ConverterParameter=Master}" 
                          Style="{StaticResource FilterChipStyle}" Margin="0,0,8,0" />
            <ComboBox SelectedIndex="0" Margin="0,0,8,0" Padding="6,2" MinWidth="160">
                <ComboBoxItem Content="{loc:Translate 'Avatar Sets Sort Name'}" />
                <ComboBoxItem Content="{loc:Translate 'Avatar Sets Sort Status'}" />
                <ComboBoxItem Content="{loc:Translate 'Avatar Sets Sort Recent'}" />
            </ComboBox>
            <Button Content="{loc:Translate 'Avatar Sets Disable All'}" Command="{Binding DisableAllCommand}" 
                    Style="{StaticResource WarnButtonStyle}" Padding="10,6" Margin="0,0,4,0" />
            <Button Content="{loc:Translate 'Avatar Sets Enable All'}" Command="{Binding EnableAllCommand}" 
                    Style="{StaticResource AccentButtonStyle}" Padding="10,6" />
        </StackPanel>
    </Border>

    <!-- Card grid (placeholder - real grid added in Task 14) -->
    <ScrollViewer Grid.Row="1" Padding="16" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <TextBlock Text="Cards go here (Task 14)" VerticalAlignment="Center" HorizontalAlignment="Center" Foreground="{DynamicResource MutedBrush}" />
    </ScrollViewer>
</Grid>
```

The `EnumEqualsConverter` is referenced - verify it exists in the project by searching for it in `App.xaml` or other XAML files. If it doesn't exist, use a simpler approach: bind the filter chips to `IsChecked` properties on the VM (e.g., `IsAllFilterActive`, `IsActiveFilterActive`) and use a `RelayCommand` to update the filter mode.

The `FilterChipStyle` and `BoolToVisibilityConverter` and `EnumEqualsConverter` should already exist in `App.xaml` resources from the Universal Triggers work. Search and verify.

- [ ] **Step 2: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded`. The toolbar XAML is added. If converters are missing, the build will report specific errors - add them to `App.xaml` resources if needed.

---

## Task 14: Add the card data template and card grid

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`

- [ ] **Step 1: Add the card data template to Window.Resources**

In `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`, inside the `<Window>` element but before the `<Border>` (or inside the Border's Resources, depending on the existing pattern), add:

```xml
<Window.Resources>
    <DataTemplate x:Key="AvatarSetCardTemplate" DataType="{x:Type vm:AvatarSetCardViewModel}">
        <Border Width="280" Height="320" Margin="0,0,12,12" 
                Background="{DynamicResource CardBackgroundBrush}" 
                BorderThickness="0" 
                CornerRadius="12" 
                Padding="0"
                Cursor="Hand">
            <Border.InputBindings>
                <MouseBinding MouseAction="LeftClick" Command="{Binding OpenEditorCommand}" />
            </Border.InputBindings>
            <Border BorderBrush="{Binding StatusStripeBrush}" BorderThickness="0,0,0,6" CornerRadius="12">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="200" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <!-- Hero image -->
                    <Border Grid.Row="0" Background="{DynamicResource ImagePlaceholderBrush}" 
                            CornerRadius="12,12,0,0" ClipToBounds="True">
                        <Grid>
                            <Image Source="{Binding Image}" Stretch="UniformToFill" 
                                   RenderOptions.BitmapScalingMode="HighQuality" 
                                   Visibility="{Binding HasAvatar, Converter={StaticResource BoolToVisibilityConverter}}" />
                            <Border BorderThickness="3" BorderBrush="{DynamicResource WarnBrush}" 
                                    CornerRadius="12,12,0,0" Margin="0"
                                    Visibility="{Binding HasAvatar, Converter={StaticResource InverseBoolToVisibilityConverter}}">
                                <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
                                    <TextBlock Text="+" FontSize="56" Foreground="{DynamicResource WarnBrush}" HorizontalAlignment="Center" />
                                    <TextBlock Text="{loc:Translate 'Avatar Sets Card Pick Avatar'}" FontWeight="Bold" Foreground="{DynamicResource WarnBrush}" FontSize="12" HorizontalAlignment="Center" />
                                </StackPanel>
                            </Border>
                            <Border Width="36" Height="36" CornerRadius="10" Background="{DynamicResource AccentDimBrush}" 
                                    HorizontalAlignment="Left" VerticalAlignment="Top" Margin="10">
                                <TextBlock Text="🎯" FontSize="18" HorizontalAlignment="Center" VerticalAlignment="Center" />
                            </Border>
                            <ToggleButton IsChecked="{Binding IsEnabled, Mode=TwoWay}" Width="38" Height="22" 
                                          HorizontalAlignment="Right" VerticalAlignment="Top" Margin="10"
                                          Style="{StaticResource CardToggleStyle}" />
                        </Grid>
                    </Border>

                    <!-- Info panel -->
                    <StackPanel Grid.Row="1" Margin="12,10,12,10">
                        <TextBlock Text="{Binding DisplayTitle}" FontWeight="Bold" FontSize="14" Foreground="{DynamicResource TextBrush}" 
                                   TextTrimming="CharacterEllipsis" />
                        <TextBlock Text="{Binding AvatarSubtitle}" FontSize="11" Foreground="{DynamicResource MutedBrush}" 
                                   TextTrimming="CharacterEllipsis" Margin="0,2,0,6" />
                        <WrapPanel Margin="0,0,0,6">
                            <Border Background="{DynamicResource AccentBrush}" CornerRadius="6" Padding="6,2" Margin="0,0,4,0">
                                <TextBlock Text="{Binding CountPillText}" Foreground="White" FontSize="9" FontWeight="Bold" />
                            </Border>
                            <Border Background="{Binding ModePillBrush}" CornerRadius="6" Padding="6,2" Margin="0,0,4,0"
                                    Visibility="{Binding ModePillText, Converter={StaticResource StringToVisibilityConverter}}">
                                <TextBlock Text="{Binding ModePillText}" Foreground="White" FontSize="9" FontWeight="Bold" />
                            </Border>
                            <Border Background="{Binding StatusStripeBrush}" CornerRadius="6" Padding="6,2" Margin="0,0,4,0">
                                <TextBlock Text="{Binding StatusPillText}" Foreground="White" FontSize="9" FontWeight="Bold" />
                            </Border>
                            <Border Background="{DynamicResource WarnBrush}" CornerRadius="6" Padding="6,2" Margin="0,0,4,0"
                                    Visibility="{Binding IsMaster, Converter={StaticResource BoolToVisibilityConverter}}">
                                <TextBlock Text="{loc:Translate 'Avatar Sets Master Badge'}" Foreground="White" FontSize="9" FontWeight="Bold" />
                            </Border>
                        </WrapPanel>
                        <Grid Margin="0,4,0,0">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="{Binding LiveText}" FontSize="10" Foreground="{Binding LiveTextBrush}" VerticalAlignment="Center" />
                            <StackPanel Grid.Column="1" Orientation="Horizontal">
                                <Button Content="{loc:Translate 'Avatar Sets Card Test'}" 
                                        Command="{Binding TestCommand}" 
                                        Style="{StaticResource SecondaryButtonStyle}" 
                                        Padding="8,3" Margin="0,0,4,0" FontSize="9"
                                        IsEnabled="{Binding CanTest}"
                                        ToolTip="Test is only available for wardrobe sets" />
                                <Button Content="{loc:Translate 'Avatar Sets Card Edit'}" 
                                        Command="{Binding OpenEditorCommand}" 
                                        Style="{StaticResource AccentButtonStyle}" 
                                        Padding="8,3" FontSize="9" />
                            </StackPanel>
                        </Grid>
                    </StackPanel>
                </Grid>
            </Border>
        </Border>
        <DataTemplate.Triggers>
            <DataTrigger Binding="{Binding IsDisabled}" Value="True">
                <Setter Property="Opacity" Value="0.55" />
            </DataTrigger>
        </DataTemplate.Triggers>
    </DataTemplate>
</Window.Resources>
```

Verify all the brushes, styles, and converters referenced exist:
- `CardBackgroundBrush`, `AccentDimBrush`, `WarnBrush`, `AccentBrush`, `MutedBrush`, `TextBrush`, `ImagePlaceholderBrush`, `PanelSubBrush`, `DividerBrush`, `InputBrush` - check `App.xaml`
- `CardToggleStyle`, `AccentButtonStyle`, `SecondaryButtonStyle`, `WarnButtonStyle`, `FilterChipStyle` - check `App.xaml`
- `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `StringToVisibilityConverter`, `EnumEqualsConverter` - check `App.xaml` for converter resource declarations

If any are missing, add them to `App.xaml` (modeling after existing similar entries). For example, `StringToVisibilityConverter` is a simple converter that returns `Visible` for non-empty strings and `Collapsed` for empty/null - add it as a resource in `App.xaml`.

- [ ] **Step 2: Replace the "Cards go here" placeholder with the real ItemsControl**

In the ScrollViewer from Task 13, replace the inner TextBlock with:

```xml
<ItemsControl ItemsSource="{Binding Cards}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel Orientation="Horizontal" />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <ContentControl Content="{Binding}" ContentTemplate="{StaticResource AvatarSetCardTemplate}" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

- [ ] **Step 3: Add the empty state**

Add this above the ItemsControl inside the ScrollViewer, with visibility bound to `CountAll`:

```xml
<StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" 
            Visibility="{Binding CountAll, Converter={StaticResource ZeroToVisibilityConverter}}"
            Margin="0,80,0,0">
    <TextBlock Text="🎯" FontSize="64" HorizontalAlignment="Center" />
    <TextBlock Text="{loc:Translate 'Avatar Sets Empty Title'}" FontWeight="Bold" FontSize="18" 
               Foreground="{DynamicResource TextBrush}" HorizontalAlignment="Center" Margin="0,16,0,8" />
    <TextBlock Text="{loc:Translate 'Avatar Sets Empty Body'}" FontSize="12" 
               Foreground="{DynamicResource MutedBrush}" HorizontalAlignment="Center" 
               TextWrapping="Wrap" MaxWidth="500" TextAlignment="Center" Margin="0,0,0,16" />
    <Button Content="{loc:Translate 'Avatar Sets Toolbar New'}" 
            Command="{Binding AddNewSetCommand}" 
            Style="{StaticResource AccentButtonStyle}" 
            HorizontalAlignment="Center" Padding="16,8" />
</StackPanel>
```

`ZeroToVisibilityConverter` returns `Visible` for zero and `Collapsed` for non-zero. If it doesn't exist, add it as a resource in `App.xaml`.

- [ ] **Step 4: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded` with possibly warnings about missing converters or styles. If any are missing, add them to `App.xaml`.

---

## Task 15: Add the slide-in editor overlay

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`

- [ ] **Step 1: Move the editor DataTemplate from MainWindow.xaml**

In `VrcTwitchOscBridge/MainWindow.xaml`, find the second `<DataTemplate DataType="{x:Type models:AvatarTriggerProfile}">` block (the editor template at lines 5419-5830, which we left in place in Task 6 because the inline UI was removed but the template might still be there).

Copy the entire DataTemplate content. It contains the editor controls (name, avatar picker, set-trigger master reward, channel point rules, wardrobe editor).

In `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`, add the copied DataTemplate to `Window.Resources`. Rename it or add a new `x:Key` like `AvatarSetEditorTemplate` so it's distinguishable from the card template.

- [ ] **Step 2: Add the slide-in overlay**

In `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`, add this Grid as the outermost element inside the main Border (sibling of the title bar Grid + content Grid):

```xml
<!-- Slide-in editor overlay -->
<Grid Grid.RowSpan="2" HorizontalAlignment="Right" Width="480"
      Visibility="{Binding IsEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}">
    <Border Background="{DynamicResource PanelShadowBrush}" Opacity="0.5" 
            MouseLeftButtonDown="OnEditorBackdropClicked" />
    <Border Background="{DynamicResource PanelBrush}" Width="480" HorizontalAlignment="Right" 
            BorderBrush="{DynamicResource DividerBrush}" BorderThickness="1,0,0,0">
        <ContentControl Content="{Binding SelectedProfile}" ContentTemplate="{StaticResource AvatarSetEditorTemplate}" />
    </Border>
</Grid>
```

The backdrop click handler closes the editor. Add a `CloseEditorCommand` binding to a transparent overlay button so the backdrop click fires it.

- [ ] **Step 3: Add the editor's bottom action bar**

At the bottom of the editor DataTemplate, add a button row:

```xml
<Grid HorizontalAlignment="Stretch" Margin="0,16,0,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <Button Grid.Column="0" Content="{loc:Translate 'Avatar Sets Delete Set'}" 
            Command="{Binding DataContext.DeleteSetCommand, RelativeSource={RelativeSource AncestorType=Window}}" 
            Style="{StaticResource WarnButtonStyle}" Padding="12,6" />
    <Button Grid.Column="2" Content="{loc:Translate 'Avatar Sets Editor Save Close'}" 
            Command="{Binding DataContext.CloseEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}" 
            Style="{StaticResource AccentButtonStyle}" Padding="16,6" />
</Grid>
```

- [ ] **Step 4: Add the backdrop click handler**

Add to `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs`:

```csharp
private void OnEditorBackdropClicked(object sender, MouseButtonEventArgs e)
{
    if (DataContext is AvatarSetsManagerViewModel vm) vm.CloseEditorCommand.Execute(null);
}
```

- [ ] **Step 5: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded`. The editor overlay is wired up. The DataTemplate from MainWindow.xaml is now hosted in the new window.

---

## Task 16: Wire up the OpenAvatarSetsManagerCommand

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Verify the command wires up the window correctly**

The `OpenAvatarSetsManagerCommand` was added in Task 5. Verify it:
1. Checks for an existing `AvatarSetsManagerWindow` and activates it if present
2. Otherwise constructs a new window with `Owner = Application.Current.MainWindow` and `DataContext = new AvatarSetsManagerViewModel(this, App.AvatarImageService)`
3. Calls `ShowDialog()`

If `App.AvatarImageService` doesn't exist as a public static property, look at how `App` exposes services - search `App.xaml.cs` for the right pattern. If the service is registered via DI, use the DI container.

- [ ] **Step 2: Build to verify**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded`. The full feature is now wired up.

---

## Task 17: Manual smoke test

**Files:** None modified - testing only.

- [ ] **Step 1: Launch the app**

Run:
```
"E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Expected: the app starts, the debug title bar shows the ` - DEBUG` suffix, no startup crash, no missing-brush warnings in the log.

- [ ] **Step 2: Open the Avatar Sets manager**

In the main window, find the "Manage Avatar Sets" button (where the inline list used to be). Click it.

Expected: a new themed window opens titled "Avatar Sets" with the title bar showing "Avatar Sets" + a subtitle. The window is centered on the main window.

- [ ] **Step 3: Verify the empty state**

If you have no existing avatar sets, the welcome card with "Create your first Avatar Set" should be visible. If you have existing sets, the cards should be visible in a grid.

- [ ] **Step 4: Add a new set**

Click "New Set" in the toolbar. The slide-in editor should open on the right with a blank editor. Close it. The card should appear in the grid with the dashed `+ PICK AVATAR` placeholder.

- [ ] **Step 5: Open the editor on a card**

Click the "Edit" button on any card. The slide-in editor should open with that set's data populated. Verify the name, avatar picker, and set-trigger master reward fields are present.

- [ ] **Step 6: Pick an avatar**

In the editor, click "Browse..." (the avatar picker button). The Avatar Picker window should open. Pick an avatar. The picker should close and the editor should show the picked avatar's name and ID. Close the editor.

Expected: the card in the grid now shows the VRChat profile icon for the picked avatar (it may take a moment to download).

- [ ] **Step 7: Toggle a set on/off**

Click the toggle on a card. The card should dim to 55% opacity, the status stripe should turn gray, and the status pill should change to "DISABLED". Click again to re-enable.

- [ ] **Step 8: Test the search box**

Type a partial set name in the search box. Only matching cards should remain visible. Clear the search. All cards return.

- [ ] **Step 9: Test the filter chips**

Click each filter chip (All / Active / Disabled / Live Now / Master). Only matching cards should be visible. The active chip should be highlighted. Click "All" to reset.

- [ ] **Step 10: Test the sort dropdown**

Click the sort dropdown and select "Sort: By Status". The cards should reorder. Switch to "Sort: Recently Edited". Switch back to "Sort: By Name".

- [ ] **Step 11: Test Enable All / Disable All**

Click "Disable All". All cards should dim. Click "Enable All". All cards should restore.

- [ ] **Step 12: Test the wardrobe mode card**

If you have a wardrobe mode set, click the "Test" button on its card. The first enabled outfit should fire (this triggers an OSC sequence in VRChat if it's running).

- [ ] **Step 13: Test the standard mode card**

Click the "Test" button on a standard mode set's card. Nothing should happen (Test is disabled for standard sets per the spec - the button should appear grayed out).

- [ ] **Step 14: Test Delete Set from the editor**

Open the editor on a card. Click "Delete Set" at the bottom. A confirmation dialog should appear. Confirm. The card should be removed from the grid and the editor should close.

- [ ] **Step 15: Test Delete All from the toolbar**

If you have multiple sets, click "Delete All" in the toolbar (it's a footer button below the grid - if not yet added, add it in this step). A confirmation dialog should appear. Confirm. All cards should be removed, the empty state should show.

- [ ] **Step 16: Close and reopen the window**

Close the window. Reopen via the Manage button. The state should persist (all your changes, deletions, additions are still there).

- [ ] **Step 17: Document any issues found**

For any step that failed, record the issue in a comment. Fix in Task 18.

---

## Task 18: Fix issues found during smoke test

**Files:** Varies based on issues.

- [ ] **Step 1: Address each issue from Task 17**

For each failure, identify the root cause (XAML binding, missing converter, missing resource, etc.), make the fix, rebuild, and re-test the specific step.

- [ ] **Step 2: Re-run the smoke test**

Re-run the full 17-step smoke test to confirm all steps pass.

---

## Task 19: Regression test

**Files:** None modified - testing only.

- [ ] **Step 1: Test Avatar Change section**

Open the Avatar Change section in the main window. Verify:
- Avatar picker still works
- Profile icons still display
- Set as current avatar still works
- Bits + Subs overrides still appear

- [ ] **Step 2: Test Power Up section**

Open the Power Up section. Verify:
- Existing rules still work
- Add Power Up still creates new rules
- Profile icons display on the power-up cards (if applicable)

- [ ] **Step 3: Test Supporter section**

Open the Supporter Growth / Supporter Override section. Verify the existing rules still work.

- [ ] **Step 4: Test Universal Triggers manager**

Click "Manage Universal Triggers". The Universal Triggers manager should still open and work unchanged. Verify:
- Cards still display
- Editor still works
- Fooma import still works
- Filter / sort still work

- [ ] **Step 5: Test Avatar Library manager**

Click "Manage Avatar Library". The library manager should still open and work unchanged.

- [ ] **Step 6: Test Twitch reward sync**

If you have a Twitch broadcaster connection, trigger a reward redemption and verify the avatar set's set-trigger master reward activates correctly. (This is the existing behavior - the spec says we didn't change it.)

- [ ] **Step 7: Document any regressions**

For any regression found, record the issue. Fix in Task 20.

---

## Task 20: Fix regressions found in Task 19

**Files:** Varies based on regressions.

- [ ] **Step 1: Address each regression**

For each regression, the cause is most likely:
- A binding in MainWindow.xaml was accidentally removed that another section still uses
- A `MainWindowViewModel` property was removed that another section still uses
- A resource in `App.xaml` was renamed

Fix the specific issue and re-test the affected section.

---

## Task 21: Theme test

**Files:** None modified - testing only.

- [ ] **Step 1: Switch to Void Crystal theme**

In the main window settings, switch the theme to "Void Crystal" (or whatever the default is). Open the Avatar Sets manager. Verify:
- The card backgrounds use the theme's `PanelBrush`
- The status stripes are visible
- The pill text is readable
- The image placeholder matches the theme

- [ ] **Step 2: Switch to a different theme**

Switch to at least one other theme (e.g., "Bubblegum" or "Cosmic Puppy Girl"). Verify the same things.

- [ ] **Step 3: Document any theme issues**

For any visual issue (text unreadable, brushes wrong color, etc.), record the issue. The fix is usually to use a different theme resource or add a new resource to `App.xaml`.

---

## Task 22: Localization audit

**Files:** None modified - testing only.

- [ ] **Step 1: Run the localization audit**

Run:
```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore
```

Expected: the audit reports no missing keys, no empty values, no placeholder mismatches for the 40 new keys across all `.extra.json` files.

- [ ] **Step 2: Address any audit failures**

If the audit reports missing keys or empty values in any language file, add the missing translations.

- [ ] **Step 3: Spot-check one non-English language**

Switch the app's display language to a non-English language (German, Spanish, French, etc.). Open the Avatar Sets manager. Verify the new strings are translated correctly and the placeholders are preserved.

---

## Task 23: Save transfer round-trip test

**Files:** None modified - testing only.

- [ ] **Step 1: Set up test data**

In the Avatar Sets manager, create 3 sets:
1. One Ready + Live set with an avatar picked
2. One No-Avatar (setup needed) set
3. One Disabled set

- [ ] **Step 2: Export the save**

Use the main window's save transfer feature to export the save to the transfer folder.

- [ ] **Step 3: Clear the local app data**

Close the app. Move `%LOCALAPPDATA%\CrystalRelay\bridge.runtime.json` (or the relevant save file) to a backup location. This simulates a clean install.

- [ ] **Step 4: Import the save and verify**

Reopen the app. Import the exported save. Verify:
- All 3 avatar sets appear
- Each set's avatar is picked correctly
- Each set's state (Ready, Setup Needed, Disabled) is preserved
- Each set's rules (channel point / wardrobe) are intact

- [ ] **Step 5: Restore the local app data**

Move the backup back to the original location.

---

## Task 24: Document the new feature

**Files:**
- Modify: `CHANGELOG.txt` (add a v3.1.9 beta or test-build entry)
- Modify: `RELEASE-CHANGE-RECORD.txt` (add notes to the in-progress entry)

Per AGENTS.md, beta and test builds include changelog entries. The exact entry depends on whether this is a test, beta, or stable release - the user decides. The entry should include:
- "Added: New dedicated Avatar Sets manager window with VRChat profile icons on each card"
- "Changed: Avatar Set management moved out of the main window into the new manager"
- "Fixed: [any specific fixes made during the implementation]"

User-facing wording, no internal workflow notes, no emoji (unless the user requests them).

---

## Task 25: Build final verification

**Files:** None modified - testing only.

- [ ] **Step 1: Clean build**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo /t:Rebuild
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Final launch**

Run:
```
"E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Expected: app starts cleanly, Avatar Sets manager opens, all features work as tested in Task 17-23.

- [ ] **Step 3: Report completion to user**

Report to the user that all 25 tasks are complete. Include:
- A short summary of what was built
- Any deviations from the plan
- Any issues found and fixed
- Any open questions for the user (e.g., translation refinements for non-English languages)
- The current active development build version (per AGENTS.md, this is v3.1.9 in the beta2 lane)
- A reminder: "Last stable: 3.1.8; active test/beta: 3.1.9" (per AGENTS.md)
