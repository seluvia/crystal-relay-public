# User-Custom Blocked Words for Twitch Chatbox

**Date:** 2026-07-08
**Status:** Design (ready for implementation)

## Purpose

Add a user-configurable banned-word list to the Twitch Chatbox settings. Users can see all hardcoded blocked words, suppress (remove) any of them, and add their own custom words — all persisted per-install.

## Architecture

### Data Model (`AppSettings.cs`)

Two new `ObservableCollection<string>` properties:
- `CustomBlockedWords` — words the user added themselves
- `SuppressedBlockedWords` — hardcoded words the user chose to disable

Persisted in `PersistedProfileSettings` as `List<string>` through the standard save/load pipeline in `SettingsStore.cs`. Default: both empty.

Suppression uses case-insensitive string comparison. If a future app update removes a hardcoded word that the user had suppressed, the suppressed entry is silently ignored.

### Filter Changes (`ChatboxRelayModerationFilter.cs`)

**`BlockedPatterns`** — currently `static readonly`, becomes `static volatile` to allow runtime rebuild when the user changes their word list.

**New public method:**

```csharp
public static void SetUserBlockList(
    IEnumerable<string> customWords,
    IEnumerable<string> suppressedWords)
```

This method:
1. Builds a case-insensitive `HashSet` from `suppressedWords`
2. Computes the effective word list: `BlockedSlurTerms.Concat(customWords).Where(not in suppressed).Concat(BlockedHarassmentPhrases)`
3. Rebuilds `blockedPatterns` from the effective list using the existing `BuildBlockedPatterns()` logic

**`ShouldBlockMessage`** logic is unchanged — it checks `blockedPatterns` then `doxxingPatterns`. When `SetUserBlockList` is called, the new patterns take effect atomically (reference swap via `volatile`).

**`DoxxingPatterns`** stays `static readonly` — not user-editable.

### BridgeCoordinator Integration

`BridgeCoordinator` calls `ChatboxRelayModerationFilter.SetUserBlockList()` in two places:
1. **On startup** — after loading settings, pass `AppSettings.CustomBlockedWords` and `AppSettings.SuppressedBlockedWords`
2. **On save** — `MainWindowViewModel` or the chatbox settings handler calls `SetUserBlockList` when the user adds/removes a word

### UI (TwitchChatboxWindow.xaml)

New collapsible "Blocked Words" section below the VRChat Chatbox panel:

```
┌──────────────────────────────────────────────┐
│  Blocked Words                         [▼]   │  ← ToggleButton header
├──────────────────────────────────────────────┤
│  [Add word...]    [+ Add]                    │  ← TextBox + Add button
├──────────────────────────────────────────────┤
│  [Restore]  nigger                           │  ← suppressed hardcoded word
│  [Restore]  chink                            │
│  [✕]       mycustomword                      │  ← user-added word
│  [✕]       anotherword                       │
│  ...                                         │
└──────────────────────────────────────────────┘
```

**DataTemplate for each item:**
- Left: `[Restore]` button (visible only for suppressed hardcoded words) or `[✕]` button (for custom words)
- Center/Right: the word text

**Above the list:** a TextBox + "Add" button to add new words. Validation: non-empty, trimmed, at least 2 characters.

**ViewModel:** The existing `ChatboxOscEnabled` area in `MainWindowViewModel` gets:
- `ObservableCollection<BlockedWordItem>` (merged from hardcoded + custom words, with a flag for which category)
- `AddBlockedWordCommand`
- `RemoveBlockedWordCommand`
- `RestoreBlockedWordCommand`
- `BlockedWordsSectionOpen` toggle

Each call to add/remove/restore also calls `ChatboxRelayModerationFilter.SetUserBlockList()` to keep the runtime filter in sync.

## Files Changed

| File | Changes |
|---|---|
| `Models/AppSettings.cs` | Add `CustomBlockedWords`, `SuppressedBlockedWords` ObservableCollections |
| `Services/SettingsStore.cs` | Add `PersistedProfileSettings` fields, load/save, defaults |
| `Services/ChatboxRelayModerationFilter.cs` | Add `SetUserBlockList()`, make `blockedPatterns` volatile |
| `Services/BridgeCoordinator.cs` | Call `SetUserBlockList()` on startup |
| `ViewModels/MainWindowViewModel.cs` | Add blocked word list, commands, merged list |
| `TwitchChatboxWindow.xaml` | Add "Blocked Words" section with list + add button |
| `TwitchChatboxWindow.xaml.cs` | Wire collapsible section |
| `VrcTwitchOscBridge.Tests/ChatboxRelayModerationFilterTests.cs` | Add tests for `SetUserBlockList` + custom/suppressed words |

## Scope

- Only word-level terms (`BlockedSlurTerms`, 33 words) are user-editable
- Harassment phrases and doxxing patterns stay hardcoded-only
- No localization keys needed (the word list itself is English, and the UI labels follow existing patterns)
