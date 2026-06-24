# FalxPlays Custom Supporter Card Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new custom supporter card for Twitch user `FalxPlays` with an Emerald Sapphire color theme (black base, emerald green + sapphire blue), an `Awooey` badge tag, and a themed emerald name glow, following the existing custom-supporter pattern.

**Architecture:** Mirror the standard custom-supporter pattern (KaiBloodwolf/Hypercraftiing/KyouZakira/Phil13938), not the special Dev inline-badge pattern. All identity/classification/name-brush logic lives in `TwitchChatMessageEntry` inside `MainWindowViewModel.cs`; all visual styling lives as resources + DataTriggers in `TwitchChatboxWindow.xaml`. The new supporter gets a `Falx` enum kind, hardcoded `Awooey` label (matching the existing non-localized custom-supporter convention), and DataTriggers in all 12 shared chatbox styles plus an inline name-glow trigger.

**Tech Stack:** C# / .NET 10 / WPF + XAML. Frozen `LinearGradientBrush`/`SolidColorBrush` for name text; XAML resource brushes + `DropShadowEffect` for card/border/rail/badge/glow.

**Spec:** `docs/superpowers/specs/2026-06-24-falxplays-supporter-card-design.md`

---

## File Structure

- **Modify:** `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` — identity/classification/brush logic inside `TwitchChatMessageEntry` (lines 21198–21818) and the `TwitchChatRoleCardKind` enum.
- **Modify:** `VrcTwitchOscBridge\TwitchChatboxWindow.xaml` — 9 new resource definitions (~line 255 area) + `IsFalxPlaysRoleCard` DataTriggers in 12 shared styles + 1 inline name-glow DataTrigger.
- **No changes to:** code-behind, moderation filter, tests, localization files.

## Notes for the Implementer

- This codebase has **no unit tests for role-card classification** — verification is a successful WPF build plus manual visual check. Do not invent a test harness; the existing four custom supporters have none.
- The Crystal Relay Developer card (`Screminpal_`) uses a **different pattern** (inline badge, `RoleCardKind.None`). Do **not** mirror the Dev pattern. Mirror KaiBloodwolf/Hypercraftiing/KyouZakira/Phil13938 only.
- The `ChatboxDevContentOffsetStyle` style does **not** get a Falx trigger — it keys off `HasBadgeRoleCard`, which already covers Falx via `RoleCardKind != None`.
- The `ChatboxRoleBadgeBorderStyle` style has **no** `IsCrystalRelayDeveloper` trigger (Dev badge is inline). Falx goes at the end of that style's triggers, after Phil13938.
- All other 11 styles list custom supporters in order: KaiBloodwolf, Hypercraftiing, KyouZakira, Phil13938, then `IsCrystalRelayDeveloper`. Insert Falx **between Phil13938 and IsCrystalRelayDeveloper** in those 11 styles to keep the named-supporters grouping together.
- Use exact line numbers from this plan; they were verified against the current file state.

---

### Task 1: C# Identity Layer — enum, login const, matcher, identity property

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:21198-21214` (enum), `:21222` (login const), `:21273` (constructor identity check), `:21348` (identity property), `:21808-21815` (matcher methods)

- [ ] **Step 1: Add `Falx` to the `TwitchChatRoleCardKind` enum**

In `MainWindowViewModel.cs`, find the enum at line 21198 and add `Falx,` after `Phil13938,`:

```csharp
public enum TwitchChatRoleCardKind
{
    None,
    KaiBloodwolf,
    Hypercraftiing,
    KyouZakira,
    Phil13938,
    Falx,
    Staff,
    LeadModerator,
    Moderator,
    Vip,
    Artist,
    TierThree,
    TierTwo,
    TierOne,
    Subscriber
}
```

- [ ] **Step 2: Add the `FalxPlaysLogin` constant**

At line 21222, after the `Phil13938Login` line, add:

```csharp
    private const string Phil13938Login = "phil13938";
    private const string FalxPlaysLogin = "falxplays";
```

- [ ] **Step 3: Add the constructor identity check**

At line 21273, after the `IsPhil13938 = IsPhil13938Account(...)` line, add:

```csharp
        IsPhil13938 = IsPhil13938Account(UserDisplayName, UserLogin);
        IsFalxPlays = IsFalxPlaysAccount(UserDisplayName, UserLogin);
```

- [ ] **Step 4: Add the `IsFalxPlays` identity property**

At line 21348, after `public bool IsPhil13938 { get; }`, add:

```csharp
    public bool IsPhil13938 { get; }

    public bool IsFalxPlays { get; }
```

- [ ] **Step 5: Add the `IsFalxPlaysAccount` and `IsFalxPlaysName` matcher methods**

At line 21815 (after the `IsPhil13938Name` method's closing brace, before `NormalizeTwitchName`), add:

```csharp
    private static bool IsFalxPlaysAccount(string displayName, string login) =>
        IsFalxPlaysName(displayName) || IsFalxPlaysName(login);

    private static bool IsFalxPlaysName(string value)
    {
        var normalized = NormalizeTwitchName(value);
        return string.Equals(normalized, FalxPlaysLogin, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTwitchName(string value) =>
```

(The `NormalizeTwitchName` line above is shown only to anchor the insertion point — do not duplicate it.)

- [ ] **Step 6: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git commit -m "Add FalxPlays identity recognition to TwitchChatMessageEntry"
```

---

### Task 2: C# Classification & Brush Layer — role-card kind, name brush, label

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:21229` (name-brush field), `:21285-21287` (RoleCardKind ternary), `:21302-21304` (NameBrush ternary), `:21366` (role-card property), `:21391` (RoleCardLabel switch), `:21770` (brush factory)

- [ ] **Step 1: Add the `FalxPlaysNameBrush` static field**

At line 21229, after the `Phil13938NameBrush` line, add:

```csharp
    private static readonly LinearGradientBrush Phil13938NameBrush = CreateFrozenPhil13938NameBrush();
    private static readonly LinearGradientBrush FalxPlaysNameBrush = CreateFrozenFalxPlaysNameBrush();
```

- [ ] **Step 2: Add the Falx branch to the `RoleCardKind` ternary**

At line 21285-21287, the ternary currently ends with:

```csharp
            : IsPhil13938
            ? TwitchChatRoleCardKind.Phil13938
            : ResolveRoleCardKind(Kind, normalizedSupportTier, BadgeSetIds);
```

Change it to insert the Falx branch between Phil13938 and `ResolveRoleCardKind`:

```csharp
            : IsPhil13938
            ? TwitchChatRoleCardKind.Phil13938
            : IsFalxPlays
            ? TwitchChatRoleCardKind.Falx
            : ResolveRoleCardKind(Kind, normalizedSupportTier, BadgeSetIds);
```

- [ ] **Step 3: Add the Falx branch to the `NameBrush` ternary**

At line 21302-21304, the ternary currently ends with:

```csharp
            : IsPhil13938
            ? Phil13938NameBrush
            : ParseNameBrush(userColor, theme);
```

Change it to insert the Falx branch between Phil13938 and `ParseNameBrush`:

```csharp
            : IsPhil13938
            ? Phil13938NameBrush
            : IsFalxPlays
            ? FalxPlaysNameBrush
            : ParseNameBrush(userColor, theme);
```

- [ ] **Step 4: Add the `IsFalxPlaysRoleCard` property**

At line 21366, after `public bool IsPhil13938RoleCard => ...`, add:

```csharp
    public bool IsPhil13938RoleCard => RoleCardKind == TwitchChatRoleCardKind.Phil13938;

    public bool IsFalxPlaysRoleCard => RoleCardKind == TwitchChatRoleCardKind.Falx;
```

- [ ] **Step 5: Add the `Falx` case to the `RoleCardLabel` switch**

At line 21391, after the `Phil13938` case, add the `Falx` case with the exact `Awooey` spelling:

```csharp
        TwitchChatRoleCardKind.Phil13938 => "The Canadian Bnuy",
        TwitchChatRoleCardKind.Falx => "Awooey",
        TwitchChatRoleCardKind.Staff => "TWITCH STAFF",
```

- [ ] **Step 6: Add the `CreateFrozenFalxPlaysNameBrush` factory method**

At line 21770, after the `CreateFrozenPhil13938NameBrush` method's closing brace, add a new factory returning a frozen horizontal `LinearGradientBrush` with emerald → sapphire stops:

```csharp
    private static LinearGradientBrush CreateFrozenFalxPlaysNameBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 191, 165), 0d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(41, 121, 255), 1d));
        brush.Freeze();
        return brush;
    }

```

- [ ] **Step 7: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git commit -m "Add FalxPlays role-card classification, name brush, and Awooey label"
```

---

### Task 3: XAML Resource Definitions — 9 brushes + glow effect

**Files:**
- Modify: `VrcTwitchOscBridge\TwitchChatboxWindow.xaml:255` (after Phil13938's GlowEffect, before `ChatboxRoleCardTextBrush`)

- [ ] **Step 1: Add the 9 FalxPlays resource definitions**

At line 255, the Phil13938 block ends with its `DropShadowEffect` (lines 251-255) followed by `<SolidColorBrush x:Key="ChatboxRoleCardTextBrush" .../>` at line 256. Insert the FalxPlays block **between** line 255 (end of Phil13938GlowEffect) and line 256 (start of ChatboxRoleCardTextBrush):

```xml
        <DropShadowEffect x:Key="Phil13938GlowEffect"
                          BlurRadius="14"
                          ShadowDepth="0"
                          Opacity="0.42"
                          Color="#E96100" />
        <SolidColorBrush x:Key="FalxPlaysTextBrush" Color="#D8F5EE" />
        <SolidColorBrush x:Key="FalxPlaysMutedBrush" Color="#6A8A8A" />
        <SolidColorBrush x:Key="FalxPlaysInsetBrush" Color="#78061418" />
        <SolidColorBrush x:Key="FalxPlaysInsetBorderBrush" Color="#8F00BFA5" />
        <LinearGradientBrush x:Key="FalxPlaysCardBrush"
                             StartPoint="0,0"
                             EndPoint="1,1">
            <GradientStop Color="#F0030608" Offset="0" />
            <GradientStop Color="#EC0A1418" Offset="0.46" />
            <GradientStop Color="#E9060C12" Offset="0.74" />
            <GradientStop Color="#ED081018" Offset="1" />
        </LinearGradientBrush>
        <LinearGradientBrush x:Key="FalxPlaysBorderBrush"
                             StartPoint="0,0"
                             EndPoint="1,1">
            <GradientStop Color="#E000BFA5" Offset="0" />
            <GradientStop Color="#E32979FF" Offset="0.5" />
            <GradientStop Color="#D800BFA5" Offset="1" />
        </LinearGradientBrush>
        <LinearGradientBrush x:Key="FalxPlaysRailBrush"
                             StartPoint="0,0"
                             EndPoint="0,1">
            <GradientStop Color="#FFFFFF" Offset="0" />
            <GradientStop Color="#00BFA5" Offset="0.5" />
            <GradientStop Color="#2979FF" Offset="1" />
        </LinearGradientBrush>
        <LinearGradientBrush x:Key="FalxPlaysBadgeBrush"
                             StartPoint="0,0"
                             EndPoint="1,1">
            <GradientStop Color="#00BFA5" Offset="0" />
            <GradientStop Color="#2979FF" Offset="1" />
        </LinearGradientBrush>
        <DropShadowEffect x:Key="FalxPlaysGlowEffect"
                          BlurRadius="14"
                          ShadowDepth="0"
                          Opacity="0.34"
                          Color="#00BFA5" />
        <SolidColorBrush x:Key="ChatboxRoleCardTextBrush" Color="#FFFDF7" />
```

(The `Phil13938GlowEffect` block above and the `ChatboxRoleCardTextBrush` line below are shown only to anchor the insertion point — do not duplicate them.)

- [ ] **Step 2: Commit**

```bash
git add "VrcTwitchOscBridge/TwitchChatboxWindow.xaml"
git commit -m "Add FalxPlays Emerald Sapphire brush and glow resources"
```

---

### Task 4: XAML Style DataTriggers — 12 styles + name glow

**Files:**
- Modify: `VrcTwitchOscBridge\TwitchChatboxWindow.xaml` — 11 styles insert Falx between Phil13938 and IsCrystalRelayDeveloper; 1 style (`ChatboxRoleBadgeBorderStyle`) appends Falx after Phil13938; 1 inline name-glow trigger added near line 1650.

Each step below adds a single DataTrigger block. Insert each block at the exact anchor shown.

- [ ] **Step 1: `ChatboxChatCardBorderStyle` — card background/border/glow**

At line 1043, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsPhil13938RoleCard}" Value="True">
                    <Setter Property="Background" Value="{StaticResource Phil13938CardBrush}" />
                    <Setter Property="BorderBrush" Value="{StaticResource Phil13938BorderBrush}" />
                    <Setter Property="BorderThickness" Value="1.5" />
                    <Setter Property="Effect" Value="{StaticResource Phil13938GlowEffect}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Background" Value="{StaticResource FalxPlaysCardBrush}" />
                    <Setter Property="BorderBrush" Value="{StaticResource FalxPlaysBorderBrush}" />
                    <Setter Property="BorderThickness" Value="1.5" />
                    <Setter Property="Effect" Value="{StaticResource FalxPlaysGlowEffect}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding IsCrystalRelayDeveloper}" Value="True">
```

(The Phil13938 trigger above and the CrystalRelayDeveloper trigger below are anchors — do not duplicate them.)

- [ ] **Step 2: `ChatboxChannelPointCardBorderStyle` — redeem card**

At line 1124, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Background" Value="{StaticResource FalxPlaysCardBrush}" />
                    <Setter Property="BorderBrush" Value="{StaticResource FalxPlaysBorderBrush}" />
                    <Setter Property="BorderThickness" Value="1.5" />
                    <Setter Property="Effect" Value="{StaticResource FalxPlaysGlowEffect}" />
                </DataTrigger>
```

- [ ] **Step 3: `ChatboxSupportCardBorderStyle` — support card**

At line 1204, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Background" Value="{StaticResource FalxPlaysCardBrush}" />
                    <Setter Property="BorderBrush" Value="{StaticResource FalxPlaysBorderBrush}" />
                    <Setter Property="BorderThickness" Value="1.5" />
                    <Setter Property="Effect" Value="{StaticResource FalxPlaysGlowEffect}" />
                </DataTrigger>
```

- [ ] **Step 4: `ChatboxTimestampTextStyle` — timestamp color**

At line 1244, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Foreground" Value="{StaticResource FalxPlaysMutedBrush}" />
                </DataTrigger>
```

- [ ] **Step 5: `ChatboxPrimaryEntryTextStyle` — primary text color**

At line 1268, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Foreground" Value="{StaticResource FalxPlaysTextBrush}" />
                </DataTrigger>
```

- [ ] **Step 6: `ChatboxMessageBodyTextStyle` — body text color**

At line 1292, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Foreground" Value="{StaticResource FalxPlaysTextBrush}" />
                </DataTrigger>
```

- [ ] **Step 7: `ChatboxMutedEntryTextStyle` — muted text color**

At line 1316, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Foreground" Value="{StaticResource FalxPlaysMutedBrush}" />
                </DataTrigger>
```

- [ ] **Step 8: `ChatboxInlinePanelStyle` — inset panel**

At line 1346, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Background" Value="{StaticResource FalxPlaysInsetBrush}" />
                    <Setter Property="BorderBrush" Value="{StaticResource FalxPlaysInsetBorderBrush}" />
                </DataTrigger>
```

- [ ] **Step 9: `ChatboxInputInlinePanelStyle` — input inset panel**

At line 1377, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Background" Value="{StaticResource FalxPlaysInsetBrush}" />
                    <Setter Property="BorderBrush" Value="{StaticResource FalxPlaysInsetBorderBrush}" />
                </DataTrigger>
```

- [ ] **Step 10: `ChatboxEventRailStyle` — event rail**

At line 1426, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Background" Value="{StaticResource FalxPlaysRailBrush}" />
                </DataTrigger>
```

- [ ] **Step 11: `ChatboxRoleRailStyle` — chat rail (visibility + brush)**

At line 1488, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before the `IsCrystalRelayDeveloper` trigger, insert:

```xml
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                    <Setter Property="Background" Value="{StaticResource FalxPlaysRailBrush}" />
                </DataTrigger>
```

- [ ] **Step 12: `ChatboxRoleBadgeBorderStyle` — role badge (append after Phil13938)**

This style has NO `IsCrystalRelayDeveloper` trigger. At line 1552, after the `IsPhil13938RoleCard` trigger's closing `</DataTrigger>` and before `</Style.Triggers>`, insert:

```xml
                <DataTrigger Binding="{Binding IsPhil13938RoleCard}" Value="True">
                    <Setter Property="Background" Value="{StaticResource Phil13938BadgeBrush}" />
                    <Setter Property="BorderBrush" Value="{StaticResource Phil13938BorderBrush}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                    <Setter Property="Background" Value="{StaticResource FalxPlaysBadgeBrush}" />
                    <Setter Property="BorderBrush" Value="{StaticResource FalxPlaysBorderBrush}" />
                </DataTrigger>
            </Style.Triggers>
```

(The Phil13938 trigger above and `</Style.Triggers>` below are anchors — do not duplicate them.)

- [ ] **Step 13: Inline name-glow DataTrigger on the chat-card name TextBlock**

At line 1650, after the `IsPhil13938RoleCard` DataTrigger's closing `</DataTrigger>` and before `</Style.Triggers>`, add an `IsFalxPlaysRoleCard` trigger with an emerald glow `DropShadowEffect`:

```xml
                                                <DataTrigger Binding="{Binding IsPhil13938RoleCard}" Value="True">
                                                    <Setter Property="Effect">
                                                        <Setter.Value>
                                                            <DropShadowEffect ShadowDepth="0" BlurRadius="3" Color="White" Opacity="0.95" />
                                                        </Setter.Value>
                                                    </Setter>
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding IsFalxPlaysRoleCard}" Value="True">
                                                    <Setter Property="Effect">
                                                        <Setter.Value>
                                                            <DropShadowEffect ShadowDepth="0" BlurRadius="3" Color="#00BFA5" Opacity="0.9" />
                                                        </Setter.Value>
                                                    </Setter>
                                                </DataTrigger>
                                            </Style.Triggers>
```

(The Phil13938 trigger above and `</Style.Triggers>` below are anchors — do not duplicate them.)

- [ ] **Step 14: Commit**

```bash
git add "VrcTwitchOscBridge/TwitchChatboxWindow.xaml"
git commit -m "Add FalxPlays DataTriggers to all chatbox styles and name glow"
```

---

### Task 5: Build Verification

**Files:** none (verification only)

- [ ] **Step 1: Build the app project**

Run:
```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: `Build succeeded` with `0 Error(s)`. If there are errors, fix them before proceeding — common issues are a misspelled resource key or a missing `Falx` enum member.

- [ ] **Step 2: Manual visual check (hand off to user)**

Tell the user:
> Build succeeded. Launch the debug build to verify the FalxPlays card:
> `E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat`
> Open the Twitch Chatbox and confirm a FalxPlays message shows: black card with emerald/sapphire gradient border + rail, `Awooey` badge, emerald→sapphire gradient name with a subtle emerald glow, across chat / redemption / support card types.

- [ ] **Step 3: Final commit if any fixes were needed**

If Step 1 or the visual check required fixes, commit them:
```bash
git add -A
git commit -m "Fix FalxPlays card build/visual issues from verification"
```

---

## Self-Review

**Spec coverage:**
- Match Twitch user `falxplays` (display + login) → Task 1 Step 5 (matcher), Step 3 (constructor call). ✓
- `Falx` enum kind → Task 1 Step 1. ✓
- `Awooey` badge label (exact casing, hardcoded) → Task 2 Step 5. ✓
- Emerald Sapphire color theme (9 resources) → Task 3 Step 1. ✓
- Name gradient (emerald → sapphire) → Task 2 Step 6 (C# factory). ✓
- Name glow on chat card → Task 4 Step 13. ✓
- All 12 styles → Task 4 Steps 1–12. ✓
- Out of scope (no localization, no code-behind, no tests, no Dev pattern, no redeem/support name glow) → respected. ✓

**Placeholder scan:** No TBD/TODO. Every code step contains complete, copy-pasteable code with exact anchors. ✓

**Type consistency:** Enum member `Falx` used consistently in Task 1 Step 1, Task 2 Steps 2/4/5. Property `IsFalxPlaysRoleCard` matches between Task 2 Step 4 (C#) and all Task 4 XAML bindings. Resource keys `FalxPlays*` match between Task 3 definitions and Task 4 setters. Login const `FalxPlaysLogin` matches between Task 1 Steps 2 and 5. ✓
