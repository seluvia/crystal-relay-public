# Bits Trigger Keyword Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the "Seconds per Bits" boxes self-explanatory and add an explicit "Require chat keyword" checkbox to the Bits Settings editor in the AvatarSwapManagerWindow's Edit Trigger panel.

**Architecture:** Add a new `BitsKeywordEnabled` boolean to `TriggerRule` with auto-migration in the `SupporterKeywordText` setter (non-empty value → enables the toggle). Add a computed `UsesBitsKeyword` property as the single runtime source of truth. Update four runtime call sites, two DTOs, the XAML, and add localization keys.

**Tech Stack:** C# / WPF / .NET 10 / xUnit

---

## File Structure

| File | Responsibility |
|---|---|
| `VrcTwitchOscBridge/Models/TriggerRule.cs` | New `BitsKeywordEnabled` property, `UsesBitsKeyword` computed, auto-sync in `SupporterKeywordText` setter |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | Four call sites swap `SupporterKeywordText` emptiness check for `UsesBitsKeyword` |
| `VrcTwitchOscBridge/Services/SettingsStore.cs` | Add `BitsKeywordEnabled` to `PersistedTriggerRule` DTO + both mapping blocks |
| `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` | Add `BitsKeywordEnabled` to `TriggerRuleSnapshot` record + mapping |
| `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml` | Relabel seconds-per-bits boxes, add hint, add "Require chat keyword" checkbox, bind `IsEnabled` on keyword textbox |
| `VrcTwitchOscBridge/Resources/Localization/en-US.json` | New keys: `Bits`, `Seconds`, `Every X bits = Y seconds`, `Require chat keyword` |
| `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs` | Round-trip and auto-sync tests for the new property |
| `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` | Test that `AddBitsRuleCommand` produces a rule with `BitsKeywordEnabled = false` |

---

## Task 1: Add `BitsKeywordEnabled` property to TriggerRule

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs:111` (add backing field after `supporterKeywordText`)
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs` (add property after `SupporterKeywordText` setter, around line 1020)
- Test: `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `TriggerRuleRoundTripTests.cs` (append at the end, before the closing `}`):

```csharp
[Fact]
public void BitsKeywordEnabled_DefaultsToFalse()
{
    var rule = new TriggerRule();
    Assert.False(rule.BitsKeywordEnabled);
}

[Fact]
public void BitsKeywordEnabled_RoundTrips()
{
    var rule = new TriggerRule { BitsKeywordEnabled = true };
    Assert.True(rule.BitsKeywordEnabled);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests.BitsKeywordEnabled"`

Expected: FAIL with `error CS1061: 'TriggerRule' does not contain a definition for 'BitsKeywordEnabled'`

- [ ] **Step 3: Add the backing field**

In `TriggerRule.cs`, add this line right after the `supporterKeywordText` field declaration (line 111, after the closing `;`):

```csharp
private bool bitsKeywordEnabled;
```

- [ ] **Step 4: Add the property**

In `TriggerRule.cs`, add this property right after the `SupporterKeywordText` setter closing `}` (the setter ends at line 1020). Find the line that ends the setter (the `}` after `RaisePropertyChanged(nameof(TriggerSummary));` and `}`) and add:

```csharp
public bool BitsKeywordEnabled
{
    get => bitsKeywordEnabled;
    set
    {
        if (SetProperty(ref bitsKeywordEnabled, value))
        {
            RaisePropertyChanged(nameof(UsesBitsKeyword));
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests.BitsKeywordEnabled"`

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Models/TriggerRule.cs" "VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add BitsKeywordEnabled property to TriggerRule"
```

---

## Task 2: Add `UsesBitsKeyword` computed property

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs` (add property after `BitsKeywordEnabled`)
- Test: `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `TriggerRuleRoundTripTests.cs`:

```csharp
[Fact]
public void UsesBitsKeyword_FalseWhenToggleOff()
{
    var rule = new TriggerRule { SupporterKeywordText = "hello", BitsKeywordEnabled = false };
    Assert.False(rule.UsesBitsKeyword);
}

[Fact]
public void UsesBitsKeyword_FalseWhenToggleOnButKeywordEmpty()
{
    var rule = new TriggerRule { SupporterKeywordText = "", BitsKeywordEnabled = true };
    Assert.False(rule.UsesBitsKeyword);
}

[Fact]
public void UsesBitsKeyword_TrueWhenToggleOnAndKeywordSet()
{
    var rule = new TriggerRule { SupporterKeywordText = "hello", BitsKeywordEnabled = true };
    Assert.True(rule.UsesBitsKeyword);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests.UsesBitsKeyword"`

Expected: FAIL with `error CS1061: 'TriggerRule' does not contain a definition for 'UsesBitsKeyword'`

- [ ] **Step 3: Add the computed property**

In `TriggerRule.cs`, add this property right after the `BitsKeywordEnabled` setter closing `}`:

```csharp
[JsonIgnore]
public bool UsesBitsKeyword
    => BitsKeywordEnabled && !string.IsNullOrWhiteSpace(SupporterKeywordText);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests.UsesBitsKeyword"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Models/TriggerRule.cs" "VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add UsesBitsKeyword computed property to TriggerRule"
```

---

## Task 3: Add auto-sync behavior to `SupporterKeywordText` setter

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs:1008-1020` (modify the `SupporterKeywordText` setter)
- Test: `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `TriggerRuleRoundTripTests.cs`:

```csharp
[Fact]
public void SupporterKeywordText_NonEmptyEnablesBitsKeyword()
{
    var rule = new TriggerRule();
    Assert.False(rule.BitsKeywordEnabled);
    rule.SupporterKeywordText = "hello";
    Assert.True(rule.BitsKeywordEnabled);
}

[Fact]
public void SupporterKeywordText_EmptyDisablesBitsKeyword()
{
    var rule = new TriggerRule { BitsKeywordEnabled = true };
    rule.SupporterKeywordText = "";
    Assert.False(rule.BitsKeywordEnabled);
}

[Fact]
public void SupporterKeywordText_WhitespaceEnablesBitsKeyword()
{
    var rule = new TriggerRule();
    rule.SupporterKeywordText = "  hello  ";
    Assert.True(rule.BitsKeywordEnabled);
    Assert.Equal("hello", rule.SupporterKeywordText);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests.SupporterKeywordText_NonEmpty|FullyQualifiedName~TriggerRuleRoundTripTests.SupporterKeywordText_Empty|FullyQualifiedName~TriggerRuleRoundTripTests.SupporterKeywordText_Whitespace"`

Expected: FAIL — `SupporterKeywordText_NonEmptyEnablesBitsKeyword` and `SupporterKeywordText_EmptyDisablesBitsKeyword` fail with `Assert.False() failed` or `Assert.True() failed`; `SupporterKeywordText_WhitespaceEnablesBitsKeyword` passes (the trim already works).

- [ ] **Step 3: Modify the setter**

In `TriggerRule.cs`, replace the `SupporterKeywordText` setter (lines 1008-1020) with:

```csharp
public string SupporterKeywordText
{
    get => supporterKeywordText;
    set
    {
        var normalizedValue = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (SetProperty(ref supporterKeywordText, normalizedValue))
        {
            BitsKeywordEnabled = !string.IsNullOrEmpty(normalizedValue);
            RaisePropertyChanged(nameof(UsesForceMovementBitsTrigger));
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests.SupporterKeywordText_NonEmpty|FullyQualifiedName~TriggerRuleRoundTripTests.SupporterKeywordText_Empty|FullyQualifiedName~TriggerRuleRoundTripTests.SupporterKeywordText_Whitespace"`

Expected: PASS

- [ ] **Step 5: Run the full TriggerRuleRoundTripTests suite to check for regressions**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests"`

Expected: PASS (all 13+ tests)

- [ ] **Step 6: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Models/TriggerRule.cs" "VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Auto-sync BitsKeywordEnabled with SupporterKeywordText setter"
```

---

## Task 4: Update `PersistedTriggerRule` DTO in SettingsStore

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:3253-3257` (add property to DTO)
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:1060` (add to rule→DTO mapping)
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:1315` (add to DTO→rule mapping)

- [ ] **Step 1: Add the property to the DTO**

In `SettingsStore.cs`, find the `PersistedTriggerRule` class (line 3153). Add the new property right after the existing `SupporterKeywordText` property (line 3257):

```csharp
public string? SupporterKeywordText { get; set; }
public bool BitsKeywordEnabled { get; set; }
```

- [ ] **Step 2: Add to the rule→DTO mapping**

In `SettingsStore.cs`, find the rule→DTO mapping block (around line 1060). Add the new mapping line right after the `SupporterKeywordText` line:

```csharp
SupporterKeywordText = rule.SupporterKeywordText,
BitsKeywordEnabled = rule.BitsKeywordEnabled,
```

- [ ] **Step 3: Add to the DTO→rule mapping**

In `SettingsStore.cs`, find the DTO→rule mapping block (around line 1315). Add the new mapping line right after the `SupporterKeywordText` line:

```csharp
SupporterKeywordText = rule.SupporterKeywordText ?? string.Empty,
BitsKeywordEnabled = rule.BitsKeywordEnabled,
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Services/SettingsStore.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add BitsKeywordEnabled to PersistedTriggerRule DTO"
```

---

## Task 5: Update `TriggerRuleSnapshot` in BridgeRuntimeConfiguration

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs:58` (add to record)
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs:134` (add to mapping)

- [ ] **Step 1: Read the record definition and mapping**

Read `BridgeRuntimeConfiguration.cs` around lines 58 and 134 to confirm the exact text.

- [ ] **Step 2: Add to the record**

In `BridgeRuntimeConfiguration.cs`, find the `TriggerRuleSnapshot` record (starts at line 58). Add the new field right after the `SupporterKeywordText` field (line 134):

```csharp
string SupporterKeywordText,
bool BitsKeywordEnabled,
```

- [ ] **Step 3: Add to the mapping**

In `BridgeRuntimeConfiguration.cs`, find the mapping that creates `TriggerRuleSnapshot` from `TriggerRule`. Add the new mapping line right after the `SupporterKeywordText` line:

```csharp
SupporterKeywordText = rule.SupporterKeywordText,
BitsKeywordEnabled = rule.BitsKeywordEnabled,
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add BitsKeywordEnabled to TriggerRuleSnapshot"
```

---

## Task 6: Update four call sites in BridgeCoordinator

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:12881`
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:13242-13248`
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:16482`
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:16657-16659`

- [ ] **Step 1: Update call site 1 (line 12881)**

Replace:
```csharp
.Where(rule => !string.IsNullOrWhiteSpace(rule.SupporterKeywordText))
```
With:
```csharp
.Where(rule => rule.UsesBitsKeyword)
```

- [ ] **Step 2: Update call site 2 (lines 13242-13248)**

Replace:
```csharp
var candidates = rules
    .Select(rule => new BitsOutfitNameCandidate(
        rule,
        rule.SupporterKeywordText.Trim(),
        NormalizeBitsOutfitPhrase(rule.SupporterKeywordText),
        NormalizeBitsOutfitCompact(rule.SupporterKeywordText)))
    .Where(candidate => !string.IsNullOrWhiteSpace(candidate.CompactName))
    .ToArray();
```
With:
```csharp
var candidates = rules
    .Where(rule => rule.UsesBitsKeyword)
    .Select(rule => new BitsOutfitNameCandidate(
        rule,
        rule.SupporterKeywordText.Trim(),
        NormalizeBitsOutfitPhrase(rule.SupporterKeywordText),
        NormalizeBitsOutfitCompact(rule.SupporterKeywordText)))
    .ToArray();
```

(Note: the `.Where(candidate => !string.IsNullOrWhiteSpace(candidate.CompactName))` filter is removed because the new `.Where(rule => rule.UsesBitsKeyword)` already guarantees the keyword is non-empty, so `CompactName` will be non-empty too.)

- [ ] **Step 3: Update call site 3 (line 16482)**

Replace:
```csharp
.Where(rule => rule.IsEnabled && IsBitsForceMovementRule(rule) && !string.IsNullOrWhiteSpace(rule.SupporterKeywordText))
```
With:
```csharp
.Where(rule => rule.IsEnabled && IsBitsForceMovementRule(rule) && rule.UsesBitsKeyword)
```

- [ ] **Step 4: Update call site 4 (lines 16657-16659)**

Replace:
```csharp
var keyword = string.IsNullOrWhiteSpace(rule.SupporterKeywordText)
    ? T("movement word")
    : rule.SupporterKeywordText.Trim();
```
With:
```csharp
var keyword = !rule.UsesBitsKeyword
    ? T("movement word")
    : rule.SupporterKeywordText.Trim();
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Use UsesBitsKeyword in BridgeCoordinator bits trigger paths"
```

---

## Task 7: Update `InlineRuleEditorControl.xaml` — relabel seconds-per-bits boxes

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml:182-188`

- [ ] **Step 1: Replace the seconds-per-bits block**

Find the existing block (lines 182-188):

```xml
<StackPanel Margin="8,0,0,0">
    <TextBlock Text="Seconds per Bits" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,0,0,2" />
    <UniformGrid Columns="2">
        <TextBox Text="{Binding Rule.BitsAmountUnitsPerDuration, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,2,0" />
        <TextBox Text="{Binding Rule.BitsSecondsPerAmountUnit, UpdateSourceTrigger=PropertyChanged}" Margin="2,0,0,0" />
    </UniformGrid>
</StackPanel>
```

Replace with:

```xml
<StackPanel Margin="8,0,0,0">
    <UniformGrid Columns="2">
        <StackPanel Margin="0,0,4,0">
            <TextBlock Text="{loc:Translate 'Bits'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,0,0,2" />
            <TextBox Text="{Binding Rule.BitsAmountUnitsPerDuration, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
        <StackPanel Margin="4,0,0,0">
            <TextBlock Text="{loc:Translate 'Seconds'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,0,0,2" />
            <TextBox Text="{Binding Rule.BitsSecondsPerAmountUnit, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
    </UniformGrid>
    <TextBlock Text="{loc:Translate 'Every X bits = Y seconds'}"
               Foreground="{DynamicResource MutedBrush}" FontSize="10"
               FontStyle="Italic" Margin="0,4,0,0" />
</StackPanel>
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Label seconds-per-bits boxes with Bits and Seconds, add formula hint"
```

---

## Task 8: Update `InlineRuleEditorControl.xaml` — add chat keyword checkbox

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml:194-195`

- [ ] **Step 1: Add the checkbox and bind IsEnabled**

Find the existing block (lines 194-195):

```xml
<TextBlock Text="Chat keyword" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,8,0,2" />
<TextBox Text="{Binding Rule.SupporterKeywordText, UpdateSourceTrigger=PropertyChanged}" />
```

Replace with:

```xml
<CheckBox IsChecked="{Binding Rule.BitsKeywordEnabled}"
          Content="{loc:Translate 'Require chat keyword'}"
          Margin="0,8,0,0" />
<TextBlock Text="Chat keyword" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,8,0,2" />
<TextBox Text="{Binding Rule.SupporterKeywordText, UpdateSourceTrigger=PropertyChanged}"
         IsEnabled="{Binding Rule.BitsKeywordEnabled}" />
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add Require chat keyword checkbox to bits settings editor"
```

---

## Task 9: Add localization keys to en-US.json

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.json`

- [ ] **Step 1: Read the en-US.json file around the "Minimum Amount" key**

Read line 134 of `en-US.json` to confirm the format and surrounding context.

- [ ] **Step 2: Add the four new keys**

Add the following four lines to `en-US.json` (anywhere in the flat key list, e.g. right after the `"Minimum Amount"` line at 134):

```json
  "Bits": "Bits",
  "Seconds": "Seconds",
  "Every X bits = Y seconds": "Every X bits = Y seconds",
  "Require chat keyword": "Require chat keyword",
```

- [ ] **Step 3: Run the localization audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" -- "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"`

Expected: Audit completes. The new `en-US` keys are recognized. Other locales will be flagged as missing these keys — that's expected and acceptable; the audit workflow handles missing translations by falling back to `en-US`. Do NOT add the keys to other locale files; the localization team will translate them later.

- [ ] **Step 4: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Resources/Localization/en-US.json"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add Bits, Seconds, formula hint, and Require chat keyword localization keys"
```

---

## Task 10: Add default-state test for AddBitsRuleCommand

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs`

- [ ] **Step 1: Add the test**

Add to `AvatarSwapManagerViewModelTests.cs` (after the existing `AddChannelPointRuleCommand_CreatesTypedInlineChannelPointRuleRowViewModel` test around line 318):

```csharp
[Fact]
public void AddBitsRuleCommand_NewRuleHasBitsKeywordEnabledFalse()
{
    var settings = new AppSettings();
    var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
    settings.AvatarSwapProfiles.Add(profile);

    var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
    vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

    vm.AddBitsRuleCommand.Execute(null);

    var rule = profile.BitsRules.Single();
    Assert.False(rule.BitsKeywordEnabled);
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarSwapManagerViewModelTests.AddBitsRuleCommand_NewRuleHasBitsKeywordEnabledFalse"`

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Test new bits rules default to BitsKeywordEnabled false"
```

---

## Task 11: Final build and full test verification

**Files:** (no changes — verification only)

- [ ] **Step 1: Build the main project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors (warnings about pre-existing nullability issues in unrelated files are OK)

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`

Expected: PASS — 138+ tests passed, 0 failed (some skipped is fine)

- [ ] **Step 3: Manual smoke test (optional but recommended)**

Launch the debug build:

```
"E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Open the Avatar Swap manager, click any avatar, click a bits trigger row, verify:
- The "Seconds per Bits" section now has labeled "Bits" and "Seconds" boxes with the "Every X bits = Y seconds" hint
- The "Require chat keyword" checkbox is present
- Unchecking the checkbox greys out the "Chat keyword" textbox
- Checking the checkbox re-enables it

If any visual issue is found, fix it in the XAML and commit before declaring done.

---

## Self-Review

**Spec coverage:**
- [x] `BitsKeywordEnabled` property — Task 1
- [x] Auto-sync in `SupporterKeywordText` setter — Task 3
- [x] `UsesBitsKeyword` computed — Task 2
- [x] UI relabel + hint — Task 7
- [x] UI checkbox + IsEnabled binding — Task 8
- [x] Four runtime call sites — Task 6
- [x] `PersistedTriggerRule` DTO — Task 4
- [x] `TriggerRuleSnapshot` DTO — Task 5
- [x] Localization keys — Task 9
- [x] Auto-sync tests — Task 3
- [x] Round-trip test — Task 1
- [x] Default-state test — Task 10

**Placeholder scan:** No TBD/TODO/placeholder patterns found. All code blocks are complete.

**Type consistency:** `BitsKeywordEnabled` and `UsesBitsKeyword` are used consistently across all tasks. The DTO field name matches the model property name in all locations.
