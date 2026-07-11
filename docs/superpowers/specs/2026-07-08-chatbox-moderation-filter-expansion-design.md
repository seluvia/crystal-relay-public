# Chatbox Moderation Filter Expansion

**Date:** 2026-07-08
**Status:** Design (ready for implementation)

## Purpose

Expand the hardcoded Twitch Chatbox relay filter from racial slurs only to also block anti-LGBTQ+ slurs, additional racial slurs, self-harm encouragement phrases, I-know-you/I-found-you harassment phrases, and doxxing patterns (phone, SSN, email). Keep the architecture as a single-file static class with no external dependencies.

## Scope

What is blocked:

| Category | Examples | Pattern type |
|---|---|---|
| Racial slurs (existing) | nigger, nigga, coon, jigaboo, ... (24 terms) | Word list → separator-bypass regex |
| Anti-LGBTQ+ slurs | faggot, fag, dyke, tranny, shemale | Word list → separator-bypass regex |
| Additional racial | nip, chingchong, yid, wop | Word list → separator-bypass regex |
| Self-harm encouragement | kys, kill/end/neck/rope yourself, go die, hope you die, you should die | Phrase list → separator-bypass regex |
| I-know-you/found-you | i know where you live, i found your address, i know your real name, i found your [anything] | Phrase list → separator-bypass regex |
| Doxxing | US phone numbers, SSNs, email addresses | Raw-text regex with \b anchors |

What is NOT blocked (explicitly allowed):
- Common non-racial profanity (fuck, shit, damn, etc.)
- Non-US phone formats
- Street addresses (too many false positives with game locations)
- Coordinate strings, order numbers, version strings

## Architecture

**Single file modified:** `Services/ChatboxRelayModerationFilter.cs`

### Changes

**Renamed lists:**
- `BlockedRacialTerms` → `BlockedSlurTerms` (existing + new single-word slurs)
- New: `BlockedHarassmentPhrases` (multi-word phrases, same regex builder)

**New list:**
- `DoxxingPatterns`: `Regex[]` compiled manually, raw-text matching

**New normalization path:**
- `NormalizeForDoxxing(string)`: NFKD + strip non-ASCII/control chars + keep digits/dashes/parens/dots/@

**Method rename:**
- `ContainsBlockedRacialContent` → `ShouldBlockMessage`
- Log message update in `BridgeCoordinator.cs`

### Pattern matching flow

```
ContainsBlockedRacialContent / ShouldBlockMessage(string? text)
├── IsNullOrWhiteSpace? → false
├── NormalizeForMatching(text) → check BlockedPatterns (slur + phrase regexes)
│   └── Match? → block
├── NormalizeForDoxxing(text) → check DoxxingPatterns (raw regexes)
│   └── Match? → block
└── → allow
```

### Bypass detection (shared)

All slurs and harassment phrases use the existing separator-bypass regex builder: a pattern is generated per collapsed term with `[^a-z]*` between each letter, plus optional trailing `s`. This means leetspeak (k!ll y0urs3lf), unicode homoglyphs, and arbitrary character insertions (k...i...l...l) are all caught.

Example: "kill yourself" → collapsed to "killyourself" → regex matches "k i l l   y o u r s e l f" with any non-letter separators between characters and between words.

### Doxxing regex patterns

Tested against raw message text with only lightweight normalization (NFKD + strip control chars):

- **Phone (US):** `\b\d{3}[-.\s]\d{3}[-.\s]\d{4}\b`
  - Requires at least one separator (dash, dot, or space) between digit groups
  - Two `\b` anchors prevent matching embedded in longer numeric strings

- **SSN:** `\b\d{3}-\d{2}-\d{4}\b`
  - Dash-separated only. Near-zero false positive.

- **Email:** `\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b`
  - Standard email regex with `\b` anchors.

### BridgeCoordinator integration

```csharp
// Rename:
private static bool ShouldBlockChatboxRelayMessage(BridgeChatMessage chatMessage)
{
    return ChatboxRelayModerationFilter.ShouldBlockMessage(chatMessage.UserDisplayName)
        || ChatboxRelayModerationFilter.ShouldBlockMessage(chatMessage.MessageText);
}
```

Log message changes from:
> "Blocked a Twitch chat relay message because it matched the zero-tolerance racial-content filter."

To:
> "Blocked a Twitch chat relay message because it matched the zero-tolerance content filter."

## Word/phrase lists (exact values for implementation)

### BlockedSlurTerms (existing 24 + 9 new = 33)
```
// Existing racial (24)
nigger, nigga, coon, jigaboo, porchmonkey, spearchucker, darkie,
golliwog, chink, gook, zipperhead, slopehead, spic, wetback, beaner,
kike, hebe, paki, raghead, towelhead, cameljockey, sandnigger, injun, redskin

// Anti-LGBTQ+ (5)
faggot, fag, dyke, tranny, shemale

// Additional racial (4)
nip, chingchong, yid, wop
```

### BlockedHarassmentPhrases (12)
```
kys
killyourself
endyourself
neckyourself
ropeyourself
justkillyourself
godie
hopeyoudie
youshoulddie
iknowwhereyoulive
ifoundyouraddress
iknowyourrealname
ifoundyourrealname
```

No prefix matching. Only full specific phrases to avoid false positives. "I know your stream" or "I found your channel" are not blocked.

## Implementation checklist

1. Modify `BlockedRacialTerms` → rename and expand
2. Add `BlockedHarassmentPhrases` array
3. Add doxxing pattern regexes
4. Add `NormalizeForDoxxing` method
5. Rename `ContainsBlockedRacialContent` → `ShouldBlockMessage`
6. Add doxxing check to `ShouldBlockMessage`
7. Update `BridgeCoordinator.cs` caller and log message
8. Build and verify
