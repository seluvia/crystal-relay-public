# Chatbox Moderation Filter Expansion — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand the hardcoded Twitch Chatbox relay filter from 24 racial slurs to also block anti-LGBTQ+ slurs, additional racial slurs, self-harm encouragement phrases, I-know-you/I-found-you harassment phrases, and doxxing patterns (US phone, SSN, email).

**Architecture:** Modify the existing static `ChatboxRelayModerationFilter` class in `Services/` — expand the word list, add a new phrase array (same regex builder as slurs), add a doxxing regex array with lighter normalization, rename the public method. Update the single call site in `BridgeCoordinator.cs`.

**Tech Stack:** C# / .NET 10 / xUnit

**Test framework:** xUnit (`[Fact]`), Assert patterns, no mocking

## Global Constraints

- No new files outside of test project (modify 2 existing files, create 1 test file)
- No external dependencies, no config UI, no Cloudflare worker
- Existing bypass detection (leet speak, unicode NFKD, arbitrary separator insertion) applies to all word/phrase patterns
- Doxxing patterns use `\b` anchors on both ends to minimize false positives
- No prefix/partial matching on harassment phrases (full phrase only)
- Common non-racial profanity stays explicitly allowed

---

### Task 1: Write ModerationFilter unit tests

**Files:**
- Create: `VrcTwitchOscBridge.Tests/ChatboxRelayModerationFilterTests.cs`
- Depends on: nothing (tests the existing class, will fail initially for new features)

**Interfaces:**
- Consumes: `ChatboxRelayModerationFilter.ShouldBlockMessage(string?)`
- Produces: test coverage for all categories

- [ ] **Step 1: Create the test file**

```csharp
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ChatboxRelayModerationFilterTests
{
    // ── Existing racial slurs still blocked ──────────────────────────

    [Fact]
    public void ExistingRacialSlurs_AreBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("nigger"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("chink"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("spic"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("kike"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("paki"));
    }

    // ── New anti-LGBTQ+ slurs ────────────────────────────────────────

    [Fact]
    public void AntiLgbtqSlurs_AreBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("faggot"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("fag"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("dyke"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("tranny"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("shemale"));
    }

    // ── New additional racial slurs ──────────────────────────────────

    [Fact]
    public void AdditionalRacialSlurs_AreBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("nip"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("chingchong"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("yid"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("wop"));
    }

    // ── Self-harm encouragement phrases ──────────────────────────────

    [Fact]
    public void SelfHarmPhrases_AreBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("kys"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("kill yourself"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("end yourself"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("neck yourself"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("rope yourself"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("just kill yourself"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("go die"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("hope you die"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("you should die"));
    }

    // ── I-know-you / I-found-you harassment phrases ──────────────────

    [Fact]
    public void StalkingHarassmentPhrases_AreBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("i know where you live"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("i found your address"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("i know your real name"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("i found your real name"));
    }

    // ── Doxxing patterns ─────────────────────────────────────────────

    [Fact]
    public void PhoneNumber_IsBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("Call me at 555-123-4567"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("555.123.4567"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("555 123 4567"));
    }

    [Fact]
    public void Ssn_IsBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("My SSN is 123-45-6789"));
    }

    [Fact]
    public void Email_IsBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("email me at user@example.com"));
    }

    // ── Bypass variants (leet speak, separators, unicode) ────────────

    [Fact]
    public void LeetSpeakSlurs_AreBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("n1gg3r"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("f4gg0t"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("tr4nny"));
    }

    [Fact]
    public void LeetSpeakPhrases_AreBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("k!ll y0urs3lf"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("k.y.s"));
    }

    [Fact]
    public void SeparatorBypass_IsBlocked()
    {
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("k.i.l.l. y.o.u.r.s.e.l.f"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("k--i--l--l   y--o--u--r--s--e--l--f"));
    }

    // ── Safe messages are NOT blocked ────────────────────────────────

    [Fact]
    public void CommonProfanity_IsAllowed()
    {
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("fuck this shit"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("what the hell"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("this game is fucking awesome"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("damn that was close"));
    }

    [Fact]
    public void NormalChat_IsAllowed()
    {
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("hello everyone"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("GG WP"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("that was a great stream"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("can someone invite me"));
    }

    [Fact]
    public void NumbersWithoutDoxxingPattern_AreAllowed()
    {
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("I got 100 kills"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("version 3.1.9 is out"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("score is 555-123"));
    }

    [Fact]
    public void HarassmentPrefixes_AreNotBlocked()
    {
        // Only full phrases are blocked, not "i know your"/"i found your" as a prefix
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("i know your stream is great"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("i found your channel"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("i know your vibe"));
    }

    [Fact]
    public void NonRacialTermsContainingSlur_AreNotBlocked()
    {
        // "chink" -> "chinking" (not a match due to \b boundaries)
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("chinking"));
    }

    [Fact]
    public void EmptyAndNull_AreNotBlocked()
    {
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage(null));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage(string.Empty));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("   "));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ChatboxRelayModerationFilterTests"`

Expected: FAIL — `ShouldBlockMessage` doesn't exist yet (still `ContainsBlockedRacialContent`), new words/phrases not in list.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge.Tests/ChatboxRelayModerationFilterTests.cs"
git commit -m "test: add ChatboxRelayModerationFilter tests for expanded filter"
```

---

### Task 2: Implement expanded filter

**Files:**
- Modify: `VrcTwitchOscBridge/Services/ChatboxRelayModerationFilter.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `ChatboxRelayModerationFilter.ShouldBlockMessage(string?)` → `bool`

- [ ] **Step 1: Replace the entire file content**

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

public static class ChatboxRelayModerationFilter
{
    // Crystal Relay intentionally allows common non-racial profanity in the
    // VRChat chat relay. This filter is only for zero-tolerance slurs,
    // self-harm encouragement, harassment/doxxing patterns, and common
    // bypass styles around them.

    private static readonly string[] BlockedSlurTerms =
    [
        // Racial (24)
        "nigger", "nigga", "coon", "jigaboo", "porchmonkey", "spearchucker",
        "darkie", "golliwog", "chink", "gook", "zipperhead", "slopehead",
        "spic", "wetback", "beaner", "kike", "hebe", "paki", "raghead",
        "towelhead", "cameljockey", "sandnigger", "injun", "redskin",

        // Anti-LGBTQ+ (5)
        "faggot", "fag", "dyke", "tranny", "shemale",

        // Additional racial (4)
        "nip", "chingchong", "yid", "wop"
    ];

    private static readonly string[] BlockedHarassmentPhrases =
    [
        "kys",
        "killyourself", "endyourself", "neckyourself", "ropeyourself",
        "justkillyourself", "godie", "hopeyoudie", "youshoulddie",
        "iknowwhereyoulive", "ifoundyouraddress",
        "iknowyourrealname", "ifoundyourrealname"
    ];

    private static readonly Regex[] BlockedPatterns = [.. BuildBlockedPatterns()];

    private static readonly Regex[] DoxxingPatterns =
    [
        // US phone: XXX-XXX-XXXX, XXX.XXX.XXXX, XXX XXX XXXX
        new(@"\b\d{3}[-.\s]\d{3}[-.\s]\d{4}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        // SSN: XXX-XX-XXXX
        new(@"\b\d{3}-\d{2}-\d{4}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        // Email: standard pattern
        new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)
    ];

    public static bool ShouldBlockMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalizedText = NormalizeForMatching(text);
        if (!string.IsNullOrWhiteSpace(normalizedText)
            && BlockedPatterns.Any(pattern => pattern.IsMatch(normalizedText)))
        {
            return true;
        }

        var doxxingText = NormalizeForDoxxing(text);
        if (!string.IsNullOrWhiteSpace(doxxingText)
            && DoxxingPatterns.Any(pattern => pattern.IsMatch(doxxingText)))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<Regex> BuildBlockedPatterns()
    {
        var allTerms = BlockedSlurTerms.Concat(BlockedHarassmentPhrases);

        foreach (var term in allTerms)
        {
            var compactTerm = CollapseToLetters(term);
            if (string.IsNullOrWhiteSpace(compactTerm))
            {
                continue;
            }

            var pieces = compactTerm
                .Select(character => Regex.Escape(character.ToString()))
                .ToArray();
            var separatorPattern = @"[^a-z]*";
            var pattern = $"(?<![a-z]){string.Join(separatorPattern, pieces)}s?(?![a-z])";
            yield return new Regex(pattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }
    }

    private static string NormalizeForMatching(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var mapped = MapCharacter(character);
            if (mapped is >= 'a' and <= 'z')
            {
                builder.Append(mapped);
            }
            else
            {
                builder.Append(' ');
            }
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    private static string NormalizeForDoxxing(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.Control
                || category == UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    private static string CollapseToLetters(string text)
    {
        var normalized = NormalizeForMatching(text);
        return new string(normalized.Where(character => character is >= 'a' and <= 'z').ToArray());
    }

    private static char MapCharacter(char character)
    {
        var lowerCharacter = char.ToLowerInvariant(character);
        return lowerCharacter switch
        {
            >= 'a' and <= 'z' => lowerCharacter,
            '0' => 'o',
            '1' => 'i',
            '3' => 'e',
            '4' => 'a',
            '5' => 's',
            '7' => 't',
            '8' => 'b',
            '9' => 'g',
            '@' => 'a',
            '$' => 's',
            '!' => 'i',
            '|' => 'i',
            '+' => 't',
            _ => ' '
        };
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ChatboxRelayModerationFilterTests"`

Expected: all PASS

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Services/ChatboxRelayModerationFilter.cs"
git commit -m "feat: expand chatbox moderation filter with slurs, harassment phrases, and doxxing patterns"
```

---

### Task 3: Update BridgeCoordinator call site

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (lines 17828-17832 and 17849)

**Interfaces:**
- Consumes: `ChatboxRelayModerationFilter.ShouldBlockMessage(string?)`
- Produces: updated method call and log message

- [ ] **Step 1: Rename the method call in `ShouldBlockChatboxRelayMessage`**

```csharp
private static bool ShouldBlockChatboxRelayMessage(BridgeChatMessage chatMessage)
{
    return ChatboxRelayModerationFilter.ShouldBlockMessage(chatMessage.UserDisplayName)
        || ChatboxRelayModerationFilter.ShouldBlockMessage(chatMessage.MessageText);
}
```

- [ ] **Step 2: Update the log message in `LogBlockedChatboxRelayMessage`**

Change:
```
"Blocked a Twitch chat relay message because it matched the zero-tolerance racial-content filter."
```
To:
```
"Blocked a Twitch chat relay message because it matched the zero-tolerance content filter."
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: build succeeds

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "refactor: update BridgeCoordinator to use renamed ShouldBlockMessage and updated log"
```

---

### Task 4: Build & run full test suite

- [ ] **Step 1: Build the main project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded

- [ ] **Step 2: Run the moderation filter tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ChatboxRelayModerationFilterTests"`

Expected: all PASS

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`

Expected: all PASS (no regressions)
