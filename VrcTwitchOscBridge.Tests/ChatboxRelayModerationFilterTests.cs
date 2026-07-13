using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

[Collection("ChatboxModerationFilter")]
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

    [Fact]
    public void PhraseTrailingS_NotBlocked()
    {
        // s? suffix is only applied to slur terms, not harassment phrases
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("ifoundyouraddresss"));
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("iknowwhereyoulives"));
    }

    [Fact]
    public void DoxxingUnicodeNormalization_IsBlocked()
    {
        // Full-width digits should normalize via NFKD
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("My SSN is \uff11\uff12\uff13-\uff14\uff15-\uff16\uff17\uff18\uff19"));
    }

    [Fact]
    public void CustomWord_IsBlocked()
    {
        ChatboxRelayModerationFilter.SetUserBlockList(["customslur"], []);
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("customslur"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("custom slur"));
        ChatboxRelayModerationFilter.SetUserBlockList([], []);
    }

    [Fact]
    public void SuppressedHardcodedWord_IsNotBlocked()
    {
        ChatboxRelayModerationFilter.SetUserBlockList([], ["nigger"]);
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("nigger"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("chink"));
        ChatboxRelayModerationFilter.SetUserBlockList([], []);
    }

    [Fact]
    public void AddedAndThenSuppressed_IsNotBlocked()
    {
        ChatboxRelayModerationFilter.SetUserBlockList(["customslur"], ["customslur"]);
        Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("customslur"));
        ChatboxRelayModerationFilter.SetUserBlockList([], []);
    }

    [Fact]
    public void ExistingSlursStillBlockedAfterReset()
    {
        ChatboxRelayModerationFilter.SetUserBlockList([], []);
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("faggot"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("dyke"));
        Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("chink"));
    }
}
