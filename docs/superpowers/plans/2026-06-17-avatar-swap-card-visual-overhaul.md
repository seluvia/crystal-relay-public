# Avatar Swap Card Visual Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the Avatar Swap manager window and its inline rule row from hardcoded hex colors to the shared `ThemeManager` palette, add a status stripe / empty-state hint / rule-count pill / hover state to the cards, fix the right-editor's Power-up button clipping, and plug the window into the live theme-change flow. Pure visual change — no behavior change.

**Architecture:** Add a `<Window.Resources>` block to `AvatarSwapManagerWindow.xaml` with the same per-window brushes + button styles + scrollbar styles that every other themed window already uses, wire the window's code-behind to `ThemeManager.ApplyToResources` so the brushes follow the active theme, swap every hardcoded hex in the window and the inline rule row to `{DynamicResource ...}` bindings, restructure the swap and roulette cards to include the new affordances, and add one new view-model property (`HasAnyRules`) plus one new localization key (`Avatar Swap Card Pick Avatar`).

**Tech Stack:** WPF + XAML, .NET 10 (`net10.0-windows`), C# 12, Visual Studio `using` style. The window's `<Window.Resources>` block pattern is copied verbatim from existing themed windows (`AvatarSetsManagerWindow.xaml`, `BugReportWindow.xaml`).

**Reference Spec:** `docs/superpowers/specs/2026-06-17-avatar-swap-card-visual-overhaul-design.md`

---

## File Structure

**Files modified (no new files):**

| File | Responsibility |
|------|----------------|
| `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` | The full window XAML: new `<Window.Resources>` block + all color migrations + card restructure + right editor + Power-up wrap + window chrome + global return banner + list-level controls. |
| `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs` | Add `ThemeManager.ApplyToResources` call in constructor, `OnThemeChanged` handler, `OnClosed` override. Pattern copied from `AvatarSetsManagerWindow.xaml.cs:14` and `:364`. |
| `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml` | Brush swap only — every hex color → `{DynamicResource ...}`. |
| `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs` | Add `HasAnyRules` property + PropertyChanged raise. |
| `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` | Add unit test for `HasAnyRules`. (Existing file; tests added in-place.) |
| `VrcTwitchOscBridge/Resources/Localization/en-US.json` | Add the new `Avatar Swap Card Pick Avatar` key. |
| `VrcTwitchOscBridge/Resources/Localization/<lang>.extra.json` (× 12) | Add matching key with translated value. |
| `CHANGELOG.txt` | Add `v3.1.10 beta 1` entry covering the visual overhaul. |
| `AGENTS.md` | Confirm `Active build lane: beta1`, `Active development build: 3.1.10` (already correct; verify only). |

**Reference files (read-only, used as templates):**
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml:85-231` — converter declarations, button styles, default TextBlock/TextBox styles, scrollbar styles.
- `VrcTwitchOscBridge/BugReportWindow.xaml:66-96` — `TitleBarButtonStyle` template.
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs:14, :364` — `ThemeManager.ApplyToResources` call sites.
- `VrcTwitchOscBridge/Services/ThemeManager.cs:1014-1062` — Void Crystal palette values for placeholder brush hex codes.

---

## Task 1: Add `HasAnyRules` property to `AvatarSwapCardViewModel` (TDD)

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs` (add property + PropertyChanged raise).
- Modify: `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` (add unit test).

- [ ] **Step 1: Read the existing test file to find a good insertion point**

Open `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` and find the test class. New test will be added at the end of the class.

- [ ] **Step 2: Write the failing test**

Append to the end of the test class:

```csharp
[Fact]
public void HasAnyRules_FalseWhenAllRuleCollectionsEmpty()
{
    var profile = new AvatarSwapProfile();
    var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

    Assert.False(vm.HasAnyRules);
}

[Fact]
public void HasAnyRules_TrueWhenChannelPointRulesPresent()
{
    var profile = new AvatarSwapProfile();
    profile.ChannelPointRules.Add(new TriggerRule());
    var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

    Assert.True(vm.HasAnyRules);
}

[Fact]
public void HasAnyRules_TrueWhenBitsRulesPresent()
{
    var profile = new AvatarSwapProfile();
    profile.BitsRules.Add(new TriggerRule());
    var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

    Assert.True(vm.HasAnyRules);
}

[Fact]
public void HasAnyRules_TrueWhenSubsRulesPresent()
{
    var profile = new AvatarSwapProfile();
    profile.SubsRules.Add(new TriggerRule());
    var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

    Assert.True(vm.HasAnyRules);
}

[Fact]
public void HasAnyRules_TrueWhenPaymentRulesPresent()
{
    var profile = new AvatarSwapProfile();
    profile.PaymentRules.Add(new TriggerRule());
    var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

    Assert.True(vm.HasAnyRules);
}
```

Note: `AvatarImageService` is a sealed class with a parameterless constructor. We construct it directly (no mocking library is used elsewhere in the test project — see the existing test pattern in this same file, which uses `new AppSettings()` and `new AvatarSwapManagerViewModel(settings)` without mocks). The `AvatarImageService` constructor creates the `AvatarIcons` and `Cache` subfolders under `AppDataPaths.ThemeAssetsFolder` if they don't exist, but does no network I/O. The tests are safe to run in CI.

`AvatarSwapProfile.Id` and `TriggerRule.Id` are `Guid` properties that auto-generate on construction — we don't set them explicitly.

Required `using` statements at the top of the test file (add if not present):
```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
```

(`VrcTwitchOscBridge.ViewModels`, `VrcTwitchOscBridge.Models`, and `Xunit` are already imported at the top of the existing file. Only `VrcTwitchOscBridge.Services` needs to be added if not present.)

- [ ] **Step 3: Run the tests to verify they fail**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~HasAnyRules"
```

Expected: All 5 tests FAIL with "HasAnyRules does not exist" (or similar compile error since the property is missing).

- [ ] **Step 4: Add the `HasAnyRules` property to the view model**

Open `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs` and add after the `UsesPaymentRules` property (around line 74):

```csharp
public bool HasAnyRules => UsesChannelPointRules || UsesBitsRules || UsesSubsRules || UsesPaymentRules;
```

Then in the `OnProfilePropertyChanged` method (around line 162-173), add `RaisePropertyChanged(nameof(HasAnyRules));` to the case block that handles rule-collection changes. The updated case block:

```csharp
case nameof(AvatarSwapProfile.ChannelPointRules):
case nameof(AvatarSwapProfile.BitsRules):
case nameof(AvatarSwapProfile.SubsRules):
case nameof(AvatarSwapProfile.PaymentRules):
    RaisePropertyChanged(nameof(AvatarSubtitle));
    RaisePropertyChanged(nameof(RuleCountText));
    RaisePropertyChanged(nameof(HasRules));
    RaisePropertyChanged(nameof(HasAnyRules));
    RaisePropertyChanged(nameof(UsesChannelPointRules));
    RaisePropertyChanged(nameof(UsesBitsRules));
    RaisePropertyChanged(nameof(UsesSubsRules));
    RaisePropertyChanged(nameof(UsesPaymentRules));
    break;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~HasAnyRules"
```

Expected: All 5 tests PASS.

- [ ] **Step 6: Run the full test suite to confirm no regressions**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```

Expected: All existing tests + the 5 new tests PASS.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs
git commit -m "feat(avatar-swap): add HasAnyRules to card view model"
```

---

## Task 2: Add new localization key to `en-US.json`

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.json`.

- [ ] **Step 1: Open `en-US.json` and find the `Avatar Sets Card Pick Avatar` key (around line 677)**

Use the existing key as a placement guide — the new key goes right after it.

- [ ] **Step 2: Add the new key on the line after `Avatar Sets Card Pick Avatar`**

The existing line:
```json
  "Avatar Sets Card Pick Avatar": "Pick Avatar",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "Pick Avatar",
```

Note: same English value as the Avatar Sets key. The new string is a drop-in for the empty-state hint shown on Avatar Swap cards when no target avatar has been picked.

- [ ] **Step 3: Verify JSON is still valid by attempting to parse**

Run:
```powershell
Get-Content -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\en-US.json" -Raw | ConvertFrom-Json | Out-Null; if ($?) { "OK" }
```

Expected output: `OK` (any error means a JSON syntax error was introduced).

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/en-US.json
git commit -m "feat(avatar-swap): add Pick Avatar localization key (en-US)"
```

---

## Task 3: Add new localization key to all non-English `.extra.json` files

**Files:**
- Modify: 12 `.extra.json` files (one per language):
  - `de-DE.extra.json`
  - `es-ES.extra.json`
  - `fr-FR.extra.json`
  - `it-IT.extra.json`
  - `ja-JP.extra.json`
  - `ko-KR.extra.json`
  - `pl-PL.extra.json`
  - `pt-BR.extra.json`
  - `ru-RU.extra.json`
  - `sv-SE.extra.json`
  - `th-TH.extra.json`
  - `zh-CN.extra.json`
  - `zh-TW.extra.json`

For each file, the translation is added as `"Avatar Swap Card Pick Avatar": "<translation>"` on a new line after the existing `"Avatar Sets Card Pick Avatar"` entry.

- [ ] **Step 1: Add the German translation**

Open `VrcTwitchOscBridge/Resources/Localization/de-DE.extra.json` and find the existing line:
```json
  "Avatar Sets Card Pick Avatar": "Avatar wählen",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "Avatar wählen",
```

- [ ] **Step 2: Add the Spanish translation**

Open `VrcTwitchOscBridge/Resources/Localization/es-ES.extra.json`. Find:
```json
  "Avatar Sets Card Pick Avatar": "Elegir avatar",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "Elegir avatar",
```

- [ ] **Step 3: Add the French translation**

Open `VrcTwitchOscBridge/Resources/Localization/fr-FR.extra.json`. Find:
```json
  "Avatar Sets Card Pick Avatar": "Choisir un avatar",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "Choisir un avatar",
```

- [ ] **Step 4: Add the Italian translation**

Open `VrcTwitchOscBridge/Resources/Localization/it-IT.extra.json`. Find:
```json
  "Avatar Sets Card Pick Avatar": "Scegli avatar",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "Scegli avatar",
```

- [ ] **Step 5: Add the Japanese translation**

Open `VrcTwitchOscBridge/Resources/Localization/ja-JP.extra.json`. Find the existing line (the source file uses UTF-8; the existing translation is "アバターを選択"):

```json
  "Avatar Sets Card Pick Avatar": "アバターを選択",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "アバターを選択",
```

- [ ] **Step 6: Add the Korean translation**

Open `VrcTwitchOscBridge/Resources/Localization/ko-KR.extra.json`. Find the existing line (existing translation is "아바타 선택"):

```json
  "Avatar Sets Card Pick Avatar": "아바타 선택",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "아바타 선택",
```

- [ ] **Step 7: Add the Polish translation**

Open `VrcTwitchOscBridge/Resources/Localization/pl-PL.extra.json`. Find:
```json
  "Avatar Sets Card Pick Avatar": "Wybierz awatara",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "Wybierz awatara",
```

- [ ] **Step 8: Add the Portuguese (Brazilian) translation**

Open `VrcTwitchOscBridge/Resources/Localization/pt-BR.extra.json`. Find:
```json
  "Avatar Sets Card Pick Avatar": "Escolher avatar",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "Escolher avatar",
```

- [ ] **Step 9: Add the Russian translation**

Open `VrcTwitchOscBridge/Resources/Localization/ru-RU.extra.json`. Find the existing line (existing translation is "Выбрать аватар"):

```json
  "Avatar Sets Card Pick Avatar": "Выбрать аватар",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "Выбрать аватар",
```

- [ ] **Step 10: Add the Swedish translation**

Open `VrcTwitchOscBridge/Resources/Localization/sv-SE.extra.json`. Find:
```json
  "Avatar Sets Card Pick Avatar": "Välj avatar",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "Välj avatar",
```

- [ ] **Step 11: Add the Thai translation**

Open `VrcTwitchOscBridge/Resources/Localization/th-TH.extra.json`. Find the existing line (existing translation is "เลือกอวาตาร์"):

```json
  "Avatar Sets Card Pick Avatar": "เลือกอวาตาร์",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "เลือกอวาตาร์",
```

- [ ] **Step 12: Add the Simplified Chinese translation**

Open `VrcTwitchOscBridge/Resources/Localization/zh-CN.extra.json`. Find the existing line (existing translation is "选择头像"):

```json
  "Avatar Sets Card Pick Avatar": "选择头像",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "选择头像",
```

- [ ] **Step 13: Add the Traditional Chinese translation**

Open `VrcTwitchOscBridge/Resources/Localization/zh-TW.extra.json`. Find the existing line (existing translation is "選擇頭像"):

```json
  "Avatar Sets Card Pick Avatar": "選擇頭像",
```

Add immediately after:
```json
  "Avatar Swap Card Pick Avatar": "選擇頭像",
```

- [ ] **Step 14: Validate all `.extra.json` files are still valid JSON**

Run:
```powershell
Get-ChildItem -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\*.extra.json" | ForEach-Object { try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json | Out-Null; "OK   {0}" -f $_.Name } catch { "FAIL {0}: {1}" -f $_.Name, $_.Exception.Message } }
```

Expected: One `OK` line per file (13 total: 12 languages + 1 en-US.extra.json). No `FAIL` lines.

- [ ] **Step 15: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/
git commit -m "feat(avatar-swap): add Pick Avatar localization key (all 12 languages)"
```

---

## Task 4: Add `ThemeManager` wiring to `AvatarSwapManagerWindow.xaml.cs`

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs`.

- [ ] **Step 1: Read the current `AvatarSwapManagerWindow.xaml.cs` to see the existing constructor**

Open the file. The constructor currently has no theme wiring. We will add three small additions.

- [ ] **Step 2: Add the `ThemeManager.ApplyToResources` call in the constructor**

In the constructor (immediately after the existing `InitializeComponent();` line), add:

```csharp
ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
ThemeManager.ThemeChanged += OnThemeChanged;
```

The complete constructor (assuming it currently looks like the standard pattern) should now be:

```csharp
public AvatarSwapManagerWindow(...)
{
    InitializeComponent();
    ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
    ThemeManager.ThemeChanged += OnThemeChanged;
}
```

(Keep the rest of the existing constructor logic untouched.)

- [ ] **Step 3: Add the `OnThemeChanged` handler method**

Add this method anywhere in the class (e.g., after the constructor):

```csharp
private void OnThemeChanged(object? sender, EventArgs e)
{
    Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
}
```

- [ ] **Step 4: Add the `OnClosed` override to unsubscribe from the theme event**

Add this method (override the base `OnClosed` from `Window`):

```csharp
protected override void OnClosed(EventArgs e)
{
    ThemeManager.ThemeChanged -= OnThemeChanged;
    base.OnClosed(e);
}
```

- [ ] **Step 5: Add the `using` for the `VrcTwitchOscBridge.Services` namespace (if not already present)**

If the file does not already have `using VrcTwitchOscBridge.Services;` at the top, add it. The `ThemeManager` class lives in that namespace.

- [ ] **Step 6: Build to verify the code compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors. (Some unrelated pre-existing warnings are fine.) No behavior change at this step because the XAML still uses hardcoded colors — only the wiring is in place.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs
git commit -m "feat(avatar-swap): wire window to ThemeManager for live theme changes"
```

---

## Task 5: Add `<Window.Resources>` block to `AvatarSwapManagerWindow.xaml`

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` (replace the existing 3-line `<Window.Resources>` block).

- [ ] **Step 1: Open the file and locate the existing `<Window.Resources>` block (lines 14-16)**

Current content (3 lines):
```xml
<Window.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVis" />
</Window.Resources>
```

- [ ] **Step 2: Replace the existing `<Window.Resources>` block with the new one**

The new block declares the placeholder brushes, default TextBlock/TextBox styles, button styles, title bar button style, scrollbar styles, and both visibility converters. Patterns are copied from `AvatarSetsManagerWindow.xaml:85-231` and `BugReportWindow.xaml:66-96`. Placeholder hex values come from the Void Crystal palette at `ThemeManager.cs:1014-1062` and are immediately overwritten by `ThemeManager.ApplyToResources` on first paint.

Replace the existing block with:

```xml
<Window.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />
    <BooleanToVisibilityConverter x:Key="InverseBoolToVisibilityConverter" />

    <FontFamily x:Key="BodyFontFamily">Verdana</FontFamily>
    <FontFamily x:Key="HeadingFontFamily">Constantia</FontFamily>

    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="#130B1E" />
    <SolidColorBrush x:Key="PanelBrush" Color="#CC1C132B" />
    <SolidColorBrush x:Key="NestedPanelBrush" Color="#B8241739" />
    <SolidColorBrush x:Key="BorderBrush" Color="#5A3D92" />
    <SolidColorBrush x:Key="AccentBrush" Color="#B16BFF" />
    <SolidColorBrush x:Key="TextBrush" Color="#F5EEFF" />
    <SolidColorBrush x:Key="MutedBrush" Color="#BFAFD8" />
    <SolidColorBrush x:Key="InputBrush" Color="#B8271A3D" />
    <SolidColorBrush x:Key="InputBorderBrush" Color="#7552BC" />
    <SolidColorBrush x:Key="SecondaryButtonBrush" Color="#2C1C48" />
    <SolidColorBrush x:Key="SecondaryButtonBorderBrush" Color="#7F5FD0" />
    <SolidColorBrush x:Key="WarnBrush" Color="#4a3a1a" />
    <SolidColorBrush x:Key="WarnBorderBrush" Color="#a08a3a" />
    <SolidColorBrush x:Key="WarnTextBrush" Color="#f0d878" />
    <SolidColorBrush x:Key="RuleCardHoverBrush" Color="#A978FF" />
    <SolidColorBrush x:Key="TitleBarBrush" Color="#20122F" />
    <SolidColorBrush x:Key="TitleBarTextBrush" Color="#F5EEFF" />
    <SolidColorBrush x:Key="TitleBarSubTextBrush" Color="#CBB9E5" />
    <SolidColorBrush x:Key="TitleBarButtonBrush" Color="#00000000" />
    <SolidColorBrush x:Key="TitleBarButtonHoverBrush" Color="#3B235B" />
    <SolidColorBrush x:Key="TitleBarButtonPressedBrush" Color="#543183" />
    <SolidColorBrush x:Key="TitleBarCloseHoverBrush" Color="#B43D62" />
    <SolidColorBrush x:Key="TitleBarClosePressedBrush" Color="#8C2648" />
    <SolidColorBrush x:Key="ScrollTrackBrush" Color="#25183D" />
    <SolidColorBrush x:Key="ScrollThumbBrush" Color="#7B57D0" />
    <SolidColorBrush x:Key="ComboTextBrush" Color="#140C20" />

    <Style TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource BodyFontFamily}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
    </Style>
    <Style TargetType="TextBox">
        <Setter Property="FontFamily" Value="{DynamicResource BodyFontFamily}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Padding" Value="6,4" />
        <Setter Property="Background" Value="{DynamicResource InputBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CaretBrush" Value="{DynamicResource TextBrush}" />
    </Style>

    <Style x:Key="SecondaryButtonStyle" TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource SecondaryButtonBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource SecondaryButtonBorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="8,3" />
        <Setter Property="Cursor" Value="Hand" />
    </Style>
    <Style x:Key="AccentButtonStyle" TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource ComboTextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="10,4" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="FontWeight" Value="Bold" />
    </Style>
    <Style x:Key="DangerButtonStyle" TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource WarnBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource WarnTextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource WarnBorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="8,3" />
        <Setter Property="Cursor" Value="Hand" />
    </Style>

    <Style x:Key="TitleBarButtonStyle" TargetType="Button">
        <Setter Property="Width" Value="40" />
        <Setter Property="Height" Value="32" />
        <Setter Property="Background" Value="{DynamicResource TitleBarButtonBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource TitleBarTextBrush}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="TitleBarButtonBorder"
                            Background="{TemplateBinding Background}"
                            CornerRadius="0">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="TitleBarButtonBorder" Property="Background" Value="{DynamicResource TitleBarButtonHoverBrush}" />
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="TitleBarButtonBorder" Property="Background" Value="{DynamicResource TitleBarButtonPressedBrush}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="ScrollBarThumbStyle" TargetType="Thumb">
        <Setter Property="MinHeight" Value="32" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Thumb">
                    <Border x:Name="ThumbChrome" Margin="2" Background="{DynamicResource ScrollThumbBrush}" CornerRadius="8" />
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="ThumbChrome" Property="Background" Value="{DynamicResource AccentBrush}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key="ScrollBarTrackButtonStyle" TargetType="RepeatButton">
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RepeatButton">
                    <Border Background="Transparent" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <ControlTemplate x:Key="VerticalScrollBarTemplate" TargetType="ScrollBar">
        <Grid Width="18" MinWidth="18" Background="Transparent">
            <Border Background="{DynamicResource ScrollTrackBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="8" />
            <Track x:Name="PART_Track" Margin="2" IsDirectionReversed="True">
                <Track.DecreaseRepeatButton>
                    <RepeatButton Style="{StaticResource ScrollBarTrackButtonStyle}" Command="{x:Static ScrollBar.PageUpCommand}" />
                </Track.DecreaseRepeatButton>
                <Track.Thumb>
                    <Thumb Style="{StaticResource ScrollBarThumbStyle}" />
                </Track.Thumb>
                <Track.IncreaseRepeatButton>
                    <RepeatButton Style="{StaticResource ScrollBarTrackButtonStyle}" Command="{x:Static ScrollBar.PageDownCommand}" />
                </Track.IncreaseRepeatButton>
            </Track>
        </Grid>
    </ControlTemplate>
    <Style TargetType="ScrollBar">
        <Setter Property="Background" Value="{DynamicResource ScrollTrackBrush}" />
        <Setter Property="Width" Value="18" />
        <Setter Property="Height" Value="Auto" />
        <Setter Property="Template" Value="{StaticResource VerticalScrollBarTemplate}" />
    </Style>
</Window.Resources>
```

- [ ] **Step 3: Build to verify the new resources compile**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors. (The XAML still uses hardcoded colors at this point, so the resources are declared but not yet referenced — that's fine; the next tasks will reference them.)

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml
git commit -m "feat(avatar-swap): add window resources block (brushes, styles, scrollbar)"
```

---

## Task 6: Migrate window chrome and global return banner

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` (lines 17-53).

- [ ] **Step 1: Replace the window outer `Border` (line 20)**

Find:
```xml
<Border Background="#1a1426" BorderBrush="#3a2a5a" BorderThickness="1">
```

Replace with:
```xml
<Border Background="{DynamicResource WindowBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1">
```

- [ ] **Step 2: Replace the title bar `Border` background (line 28)**

Find:
```xml
<Border Grid.Row="0" Background="#241a3a" MouseLeftButtonDown="OnTitleBarMouseDown">
```

Replace with:
```xml
<Border Grid.Row="0" Background="{DynamicResource TitleBarBrush}" MouseLeftButtonDown="OnTitleBarMouseDown">
```

- [ ] **Step 3: Update the "Avatar Swap" title `TextBlock` (line 30)**

Find:
```xml
<TextBlock Text="Avatar Swap" Foreground="#e8e3f5" FontWeight="SemiBold" VerticalAlignment="Center" Margin="12,0" />
```

Replace with:
```xml
<TextBlock Text="Avatar Swap" Foreground="{DynamicResource TitleBarTextBrush}" FontFamily="{DynamicResource HeadingFontFamily}" FontWeight="SemiBold" VerticalAlignment="Center" Margin="12,0" />
```

- [ ] **Step 4: Update the close button (line 31)**

Find:
```xml
<Button DockPanel.Dock="Right" Content="✕" Width="40" Height="32" Background="Transparent" BorderThickness="0" Foreground="#e8e3f5" Click="OnCloseClicked" WindowChrome.IsHitTestVisibleInChrome="True" />
```

Replace with:
```xml
<Button DockPanel.Dock="Right" Content="✕" Style="{DynamicResource TitleBarButtonStyle}" Click="OnCloseClicked" WindowChrome.IsHitTestVisibleInChrome="True" />
```

- [ ] **Step 5: Update the global return avatar banner (lines 42-53)**

Find:
```xml
<Border Grid.Row="0" Background="#241a3a" BorderBrush="#3a2a5a" BorderThickness="1" CornerRadius="6" Padding="8" Margin="0,0,0,10">
    <DockPanel>
        <TextBlock DockPanel.Dock="Top" Text="↩ RETURN AVATAR (used by all swaps + roulettes)" Foreground="#9b86c9" FontSize="10" Margin="0,0,0,4" />
        <StackPanel Orientation="Horizontal">
            <Border Width="32" Height="32" Background="#3a2a5a" CornerRadius="5" Margin="0,0,8,0" />
            <TextBlock Text="{Binding GlobalReturnAvatarName}" VerticalAlignment="Center" Margin="0,0,12,0" />
            <Button Content="Pick…" Click="OnPickGlobalReturnClicked" Padding="8,4" Margin="0,0,4,0" />
            <Button Content="Use Current" Click="OnUseCurrentAvatarForGlobalReturnClicked" Padding="8,4" Margin="0,0,4,0" />
            <Button Content="Clear" Command="{Binding ClearGlobalReturnCommand}" Padding="8,4" />
        </StackPanel>
    </DockPanel>
</Border>
```

Replace with:
```xml
<Border Grid.Row="0" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="8" Margin="0,0,0,10">
    <DockPanel>
        <TextBlock DockPanel.Dock="Top" Text="↩ RETURN AVATAR (used by all swaps + roulettes)" Foreground="{DynamicResource MutedBrush}" FontSize="10" Margin="0,0,0,4" />
        <StackPanel Orientation="Horizontal">
            <Border Width="32" Height="32" Background="{DynamicResource NestedPanelBrush}" CornerRadius="5" Margin="0,0,8,0" />
            <TextBlock Text="{Binding GlobalReturnAvatarName}" VerticalAlignment="Center" Margin="0,0,12,0" />
            <Button Content="Pick…" Click="OnPickGlobalReturnClicked" Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,4,0" />
            <Button Content="Use Current" Click="OnUseCurrentAvatarForGlobalReturnClicked" Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,4,0" />
            <Button Content="Clear" Command="{Binding ClearGlobalReturnCommand}" Style="{StaticResource SecondaryButtonStyle}" />
        </StackPanel>
    </DockPanel>
</Border>
```

- [ ] **Step 6: Build to verify the window chrome and banner render correctly**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml
git commit -m "feat(avatar-swap): migrate window chrome and global return banner to theme"
```

---

## Task 7: Migrate list-level controls (headings + add buttons)

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` (lines 64, 95, 97, 119).

- [ ] **Step 1: Update the "Avatar Swaps" section heading (line 64)**

Find:
```xml
<TextBlock Text="Avatar Swaps" FontSize="10" Foreground="#b0a3d0" Margin="0,0,0,6" />
```

Replace with:
```xml
<TextBlock Text="Avatar Swaps" FontSize="10" FontWeight="SemiBold" Foreground="{DynamicResource MutedBrush}" Margin="0,0,0,6" />
```

- [ ] **Step 2: Update the "+ Add Avatar" button (line 95)**

Find:
```xml
<Button Content="+ Add Avatar" Command="{Binding AddSwapCommand}" HorizontalAlignment="Left" Padding="10,5" Margin="0,6,0,14" />
```

Replace with:
```xml
<Button Content="+ Add Avatar" Command="{Binding AddSwapCommand}" Style="{StaticResource SecondaryButtonStyle}" HorizontalAlignment="Left" Padding="10,5" Margin="0,6,0,14" />
```

- [ ] **Step 3: Update the "🎰 Avatar Roulette" section heading (line 97)**

Find:
```xml
<TextBlock Text="🎰 Avatar Roulette" FontSize="10" Foreground="#d4af37" Margin="0,0,0,6" />
```

Replace with:
```xml
<TextBlock Text="🎰 Avatar Roulette" FontSize="10" FontWeight="SemiBold" Foreground="{DynamicResource WarnBrush}" Margin="0,0,0,6" />
```

- [ ] **Step 4: Update the "+ Add Roulette" button (line 119)**

Find:
```xml
<Button Content="+ Add Roulette" Command="{Binding AddRouletteCommand}" HorizontalAlignment="Left" Padding="10,5" Margin="0,6,0,0" />
```

Replace with:
```xml
<Button Content="+ Add Roulette" Command="{Binding AddRouletteCommand}" Style="{StaticResource SecondaryButtonStyle}" HorizontalAlignment="Left" Padding="10,5" Margin="0,6,0,0" />
```

- [ ] **Step 5: Build to verify the list-level controls render correctly**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml
git commit -m "feat(avatar-swap): migrate list-level controls to theme"
```

---

## Task 8: Restructure the Avatar Swap card with status stripe, empty state, rule-count pill, and hover

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` (lines 71-93 — the swap card `DataTemplate`).

- [ ] **Step 1: Find the existing swap card `DataTemplate` (lines 71-93)**

Current content (the `Border` with `Width="180" Height="130" Background="#322250"` etc., wrapping the `Grid` with the three row definitions):

```xml
<Border Width="180" Height="130" Background="#322250" BorderBrush="#4a3868" BorderThickness="1" CornerRadius="7" Margin="0,0,6,6" Padding="6" ClipToBounds="True" Cursor="Hand">
    <Border.InputBindings>
        <MouseBinding MouseAction="LeftClick" Command="{Binding DataContext.OpenSwapEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" />
    </Border.InputBindings>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="64" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <Border Grid.Row="0" Background="#3a2a5a" CornerRadius="4" ClipToBounds="True">
            <Grid>
                <Image Source="{Binding Image}" Stretch="UniformToFill" RenderOptions.BitmapScalingMode="HighQuality" />
            </Grid>
        </Border>
        <TextBlock Grid.Row="1" Text="{Binding Profile.TargetAvatarName}" FontWeight="SemiBold" FontSize="11" Margin="0,4,0,0" TextTrimming="CharacterEllipsis" />
        <TextBlock Grid.Row="2" Text="{Binding AvatarSubtitle}" Foreground="#b0a3d0" FontSize="9" TextTrimming="CharacterEllipsis" />
    </Grid>
</Border>
```

- [ ] **Step 2: Replace with the new themed card**

```xml
<Border Width="180" Height="130" Background="{DynamicResource PanelBrush}" BorderBrush="{Binding StatusStripeBrush}" BorderThickness="4,1,1,1" CornerRadius="7" Margin="0,0,6,6" Padding="6" ClipToBounds="True" Cursor="Hand">
    <Border.InputBindings>
        <MouseBinding MouseAction="LeftClick" Command="{Binding DataContext.OpenSwapEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" />
    </Border.InputBindings>
    <Border.Style>
        <Style TargetType="Border">
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="BorderBrush" Value="{DynamicResource RuleCardHoverBrush}" />
                </Trigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="64" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="{DynamicResource NestedPanelBrush}" CornerRadius="4" ClipToBounds="True">
            <Grid>
                <Image Source="{Binding Image}" Stretch="UniformToFill" RenderOptions.BitmapScalingMode="HighQuality"
                       Visibility="{Binding HasTarget, Converter={StaticResource BoolToVisibilityConverter}}" />
                <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center"
                            Visibility="{Binding HasTarget, Converter={StaticResource InverseBoolToVisibilityConverter}}">
                    <TextBlock Text="🎭" FontSize="24" HorizontalAlignment="Center" Foreground="{DynamicResource MutedBrush}" />
                    <TextBlock Text="{loc:Translate 'Avatar Swap Card Pick Avatar'}" FontSize="10" HorizontalAlignment="Center" Margin="0,2,0,0" Foreground="{DynamicResource MutedBrush}" />
                </StackPanel>
                <Border HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,4,4,0"
                        Background="{DynamicResource AccentBrush}" CornerRadius="8" Padding="6,1"
                        Visibility="{Binding HasAnyRules, Converter={StaticResource BoolToVisibilityConverter}}">
                    <TextBlock Text="{Binding RuleCountText}" FontSize="9" FontWeight="Bold" Foreground="{DynamicResource ComboTextBrush}" />
                </Border>
            </Grid>
        </Border>

        <TextBlock Grid.Row="1" Text="{Binding Profile.TargetAvatarName}" FontWeight="SemiBold" FontSize="12" Margin="0,4,0,0" TextTrimming="CharacterEllipsis" ToolTip="{Binding Profile.TargetAvatarName}" />
        <TextBlock Grid.Row="2" Text="{Binding AvatarSubtitle}" Foreground="{DynamicResource MutedBrush}" FontSize="10" TextTrimming="CharacterEllipsis" ToolTip="{Binding AvatarSubtitle}" />
    </Grid>
</Border>
```

Note on the border brush logic: `BorderBrush="{Binding StatusStripeBrush}"` binds the whole border to the status color, but `BorderThickness="4,1,1,1"` makes the left edge 4px and the others 1px. The 4px left edge visually dominates, creating the "stripe" effect. The hover `Trigger` overrides the entire brush to `RuleCardHoverBrush` on mouse-over. This matches the pattern in `AvatarSetsManagerWindow.xaml:480-491`.

- [ ] **Step 3: Add the `loc` namespace declaration to the window root if not already present**

The `loc:Translate` markup extension requires `xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services"`. Open the file root and verify this namespace is declared. If not, add it.

Current root (line 1-13):
```xml
<Window x:Class="VrcTwitchOscBridge.AvatarSwapManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:uc="clr-namespace:VrcTwitchOscBridge.UserControls"
        Title="Avatar Swap"
        ...
```

If `xmlns:loc` is missing, add it. After the edit:
```xml
<Window x:Class="VrcTwitchOscBridge.AvatarSwapManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:uc="clr-namespace:VrcTwitchOscBridge.UserControls"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services"
        Title="Avatar Swap"
        ...
```

- [ ] **Step 4: Build to verify the new card structure compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml
git commit -m "feat(avatar-swap): restructure swap card with status stripe, empty state, rule-count pill, hover"
```

---

## Task 9: Restructure the Roulette card

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` (lines 98-117 — the roulette card `DataTemplate`).

- [ ] **Step 1: Find the existing roulette card `DataTemplate` (lines 104-116)**

Current content:
```xml
<Border Width="180" Height="130" Background="#322250" BorderBrush="#d4af37" BorderThickness="1.5" CornerRadius="7" Margin="0,0,6,6" Padding="6" ClipToBounds="True" Cursor="Hand">
    <Border.InputBindings>
        <MouseBinding MouseAction="LeftClick" Command="{Binding DataContext.OpenRouletteEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" />
    </Border.InputBindings>
    <StackPanel>
        <Border Height="64" Background="#3a2a5a" CornerRadius="4" ClipToBounds="True" />
        <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="11" Margin="0,4,0,0" TextTrimming="CharacterEllipsis" />
        <TextBlock Text="{Binding Subtitle}" Foreground="#d4af37" FontSize="9" TextTrimming="CharacterEllipsis" />
    </StackPanel>
</Border>
```

- [ ] **Step 2: Replace with the new themed card**

The roulette card mirrors the swap card's structure but uses `WarnBrush` for the border (preserving the gold accent), has no empty-state hint, and has no rule-count pill (roulette triggers are configured in the right editor).

```xml
<Border Width="180" Height="130" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource WarnBrush}" BorderThickness="4,1,1,1" CornerRadius="7" Margin="0,0,6,6" Padding="6" ClipToBounds="True" Cursor="Hand">
    <Border.InputBindings>
        <MouseBinding MouseAction="LeftClick" Command="{Binding DataContext.OpenRouletteEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" />
    </Border.InputBindings>
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="BorderBrush" Value="{DynamicResource WarnBrush}" />
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="BorderBrush" Value="{DynamicResource RuleCardHoverBrush}" />
                </Trigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <StackPanel>
        <Border Height="64" Background="{DynamicResource NestedPanelBrush}" CornerRadius="4" ClipToBounds="True" />
        <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="12" Margin="0,4,0,0" TextTrimming="CharacterEllipsis" ToolTip="{Binding Name}" />
        <TextBlock Text="{Binding Subtitle}" Foreground="{DynamicResource WarnBrush}" FontSize="10" TextTrimming="CharacterEllipsis" ToolTip="{Binding Subtitle}" />
    </StackPanel>
</Border>
```

- [ ] **Step 3: Build to verify the new roulette card compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml
git commit -m "feat(avatar-swap): restructure roulette card with theme"
```

---

## Task 10: Migrate right editor panel (swap) + Power-up WrapPanel fix

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` (lines 122-195 — the right swap editor `Border`).

- [ ] **Step 1: Find the existing right swap editor `Border` (lines 122-195)**

Current content (the `Border` with `Background="#241a3a"`, the avatar header, the four trigger `ItemsControl`s, the advanced triggers `StackPanel`, and the Delete/Save button row).

- [ ] **Step 2: Replace the outer editor `Border` and the avatar header**

Find:
```xml
<Border Grid.Column="1" Background="#241a3a" BorderBrush="#3a2a5a" BorderThickness="1" CornerRadius="6" Padding="10"
        Visibility="{Binding IsSwapEditorOpen, Converter={StaticResource BoolToVis}}">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel>
            <DockPanel Margin="0,0,0,8">
                <Border Width="40" Height="40" Background="#3a2a5a" CornerRadius="5" Margin="0,0,8,0" ClipToBounds="True">
                    <Image Source="{Binding SelectedSwapCard.Image}" Stretch="UniformToFill" />
                </Border>
                <StackPanel VerticalAlignment="Center">
                    <TextBlock Text="{Binding SelectedSwapCard.Profile.TargetAvatarName}" FontWeight="SemiBold" FontSize="13" TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="Target Avatar" Foreground="#9b86c9" FontSize="10" />
                </StackPanel>
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <Button Content="Browse" Click="OnPickTargetAvatarClicked" Padding="6,3" Margin="0,0,4,0" />
                    <Button Content="Use Current" Click="OnUseCurrentAvatarForTargetClicked" Padding="6,3" />
                </StackPanel>
            </DockPanel>
            <TextBlock Text="↩ Returns to global return avatar" Foreground="#9b86c9" FontSize="10" Margin="0,0,0,8" />
```

Replace with:
```xml
<Border Grid.Column="1" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10"
        Visibility="{Binding IsSwapEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel>
            <DockPanel Margin="0,0,0,8">
                <Border Width="40" Height="40" Background="{DynamicResource NestedPanelBrush}" CornerRadius="5" Margin="0,0,8,0" ClipToBounds="True">
                    <Image Source="{Binding SelectedSwapCard.Image}" Stretch="UniformToFill" />
                </Border>
                <StackPanel VerticalAlignment="Center">
                    <TextBlock Text="{Binding SelectedSwapCard.Profile.TargetAvatarName}" FontWeight="SemiBold" FontSize="13" TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="Target Avatar" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                </StackPanel>
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <Button Content="Browse" Click="OnPickTargetAvatarClicked" Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,4,0" />
                    <Button Content="Use Current" Click="OnUseCurrentAvatarForTargetClicked" Style="{StaticResource SecondaryButtonStyle}" />
                </StackPanel>
            </DockPanel>
            <TextBlock Text="↩ Returns to global return avatar" Foreground="{DynamicResource MutedBrush}" FontSize="10" Margin="0,0,0,8" />
```

- [ ] **Step 3: Update the four section headings and the "+ Add …" buttons (lines 142-180)**

For each of the four section heading TextBlocks ("🏆 Channel Points", "💎 Bits", "⭐ Subs", "💵 Payment") and the corresponding "+ Add …" Button:

Find each pair in the original. For example:
```xml
<TextBlock Text="🏆 Channel Points" FontWeight="SemiBold" FontSize="11" Margin="0,0,0,4" />
...
<Button Content="+ Add Channel Point" Command="{Binding AddChannelPointRuleCommand}" HorizontalAlignment="Left" Padding="6,3" Margin="0,4,0,8" />
```

Replace the heading pattern across all four sections with:
```xml
<TextBlock Text="🏆 Channel Points" FontWeight="SemiBold" FontSize="12" Margin="0,0,0,4" />
```

Replace the button pattern across all four sections with:
```xml
<Button Content="+ Add Channel Point" Command="{Binding AddChannelPointRuleCommand}" Style="{StaticResource SecondaryButtonStyle}" HorizontalAlignment="Left" Margin="0,4,0,8" />
```

(Apply the analogous changes to the Bits, Subs, and Payment sections — only the content strings and `Command` binding change.)

- [ ] **Step 4: Update the advanced triggers label and the button row (lines 182-187)**

Find:
```xml
<TextBlock Text="Advanced triggers (open full editor)" Foreground="#9b86c9" FontSize="10" Margin="0,8,0,4" />
<StackPanel Orientation="Horizontal">
    <Button Content="💬 Chat Command" Command="{Binding AddAdvancedTriggerCommand}" CommandParameter="ChatCommand" Padding="6,3" Margin="0,0,4,0" />
    <Button Content="👥 Follow" Command="{Binding AddAdvancedTriggerCommand}" CommandParameter="Follow" Padding="6,3" Margin="0,0,4,0" />
    <Button Content="⚡ Power-up" Command="{Binding AddAdvancedTriggerCommand}" CommandParameter="PowerUp" Padding="6,3" />
</StackPanel>
```

Replace with:
```xml
<TextBlock Text="Advanced triggers (open full editor)" Foreground="{DynamicResource MutedBrush}" FontSize="10" Margin="0,8,0,4" />
<WrapPanel Orientation="Horizontal">
    <Button Content="💬 Chat Command" Command="{Binding AddAdvancedTriggerCommand}" CommandParameter="ChatCommand" Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,4,4" />
    <Button Content="👥 Follow" Command="{Binding AddAdvancedTriggerCommand}" CommandParameter="Follow" Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,4,4" />
    <Button Content="⚡ Power-up" Command="{Binding AddAdvancedTriggerCommand}" CommandParameter="PowerUp" Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,4,4" />
</WrapPanel>
```

Note: `StackPanel` → `WrapPanel` is the Power-up clipping fix.

- [ ] **Step 5: Update the Delete/Save button row (lines 189-192)**

Find:
```xml
<StackPanel Orientation="Horizontal" Margin="0,12,0,0">
    <Button Content="Delete Avatar" Command="{Binding DeleteSwapCommand}" Padding="8,4" Background="#5a2a2a" Foreground="#f0a0a0" />
    <Button Content="Save" Command="{Binding SaveSwapEditorCommand}" Padding="8,4" Margin="6,0,0,0" />
</StackPanel>
```

Replace with:
```xml
<StackPanel Orientation="Horizontal" Margin="0,12,0,0">
    <Button Content="Delete Avatar" Command="{Binding DeleteSwapCommand}" Style="{StaticResource DangerButtonStyle}" />
    <Button Content="Save" Command="{Binding SaveSwapEditorCommand}" Style="{StaticResource AccentButtonStyle}" Margin="6,0,0,0" />
</StackPanel>
```

- [ ] **Step 6: Build to verify the swap editor panel compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml
git commit -m "feat(avatar-swap): migrate right swap editor to theme + Power-up WrapPanel fix"
```

---

## Task 11: Migrate right editor panel (roulette) + migrate `InlineAvatarSwapRuleRowControl.xaml`

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` (lines 197-232 — the right roulette editor `Border`).
- Modify: `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`.

- [ ] **Step 1: Find the existing right roulette editor `Border` (lines 197-232)**

Current content starts with `<Border Grid.Column="1" Background="#241a3a" ... Visibility="{Binding IsRouletteEditorOpen, Converter={StaticResource BoolToVis}}">` and contains the Roulette title, the Pool `ItemsControl` with 60×60 tiles, the Triggers `ItemsControl`, and the Delete Roulette / Save button row.

- [ ] **Step 2: Replace the roulette editor `Border` outer chrome**

Find:
```xml
<Border Grid.Column="1" Background="#241a3a" BorderBrush="#3a2a5a" BorderThickness="1" CornerRadius="6" Padding="10"
        Visibility="{Binding IsRouletteEditorOpen, Converter={StaticResource BoolToVis}}">
```

Replace with:
```xml
<Border Grid.Column="1" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10"
        Visibility="{Binding IsRouletteEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}">
```

- [ ] **Step 3: Update the Roulette title (line 202)**

Find:
```xml
<TextBlock Text="Roulette" FontWeight="SemiBold" FontSize="13" Margin="0,0,0,8" />
```

Replace with:
```xml
<TextBlock Text="Roulette" FontWeight="SemiBold" FontSize="13" Margin="0,0,0,8" />
```

(Already uses TextBrush by default through the default TextBlock style. No change needed unless the Foreground isn't default — leave as-is.)

- [ ] **Step 4: Update the Pool/Triggers headings and pool tiles (lines 203-215)**

Find:
```xml
<TextBlock Text="Pool" FontWeight="SemiBold" FontSize="11" Margin="0,0,0,4" />
<ItemsControl ItemsSource="{Binding SelectedRouletteCard.Roulette.Pool}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Width="60" Height="60" Background="#3a2a5a" CornerRadius="3" Margin="0,0,4,4" ClipToBounds="True" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>

<TextBlock Text="Triggers" FontWeight="SemiBold" FontSize="11" Margin="0,8,0,4" />
```

Replace with:
```xml
<TextBlock Text="Pool" FontWeight="SemiBold" FontSize="12" Margin="0,0,0,4" />
<ItemsControl ItemsSource="{Binding SelectedRouletteCard.Roulette.Pool}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Width="60" Height="60" Background="{DynamicResource NestedPanelBrush}" CornerRadius="3" Margin="0,0,4,4" ClipToBounds="True" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>

<TextBlock Text="Triggers" FontWeight="SemiBold" FontSize="12" Margin="0,8,0,4" />
```

- [ ] **Step 5: Update the Delete Roulette / Save row (lines 226-229)**

Find:
```xml
<StackPanel Orientation="Horizontal" Margin="0,12,0,0">
    <Button Content="Delete Roulette" Command="{Binding DeleteRouletteCommand}" Padding="8,4" Background="#5a2a2a" Foreground="#f0a0a0" />
    <Button Content="Save" Command="{Binding SaveRouletteEditorCommand}" Padding="8,4" Margin="6,0,0,0" />
</StackPanel>
```

Replace with:
```xml
<StackPanel Orientation="Horizontal" Margin="0,12,0,0">
    <Button Content="Delete Roulette" Command="{Binding DeleteRouletteCommand}" Style="{StaticResource DangerButtonStyle}" />
    <Button Content="Save" Command="{Binding SaveRouletteEditorCommand}" Style="{StaticResource AccentButtonStyle}" Margin="6,0,0,0" />
</StackPanel>
```

- [ ] **Step 6: Build to verify the roulette editor compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 7: Open `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`**

The file is 37 lines. We will swap every hex color for a `{DynamicResource ...}` binding and bump the `LabelStyle.FontSize` from 9 to 10.

- [ ] **Step 8: Replace the entire content of the file**

Find the current content (lines 5-19 hold the `<UserControl.Resources>` block, line 21 holds the collapsed row `Border`, line 30 holds the expanded editor `Border`).

Replace the full file content (excluding the `x:Class` declaration at the top — keep that) with:

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.InlineAvatarSwapRuleRowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis" />
        <Style TargetType="TextBlock" x:Key="LabelStyle">
            <Setter Property="Foreground" Value="{DynamicResource MutedBrush}" />
            <Setter Property="FontSize" Value="10" />
            <Setter Property="Margin" Value="0,0,0,2" />
        </Style>
        <Style TargetType="TextBox" x:Key="InputStyle">
            <Setter Property="Background" Value="{DynamicResource InputBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}" />
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            <Setter Property="Padding" Value="3" />
            <Setter Property="FontSize" Value="11" />
        </Style>
    </UserControl.Resources>
    <StackPanel>
        <Border Background="{DynamicResource PanelBrush}" Padding="4,3" CornerRadius="3" Margin="0,0,0,2">
            <DockPanel>
                <TextBlock Text="{Binding Summary}" FontSize="11" />
                <Button DockPanel.Dock="Right" Content="🗑" Command="{Binding DataContext.DeleteRuleCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" Width="20" Background="Transparent" Foreground="{DynamicResource MutedBrush}" BorderThickness="0" />
            </DockPanel>
            <Border.InputBindings>
                <MouseBinding MouseAction="LeftClick" Command="{Binding DataContext.BeginInlineEditCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" />
            </Border.InputBindings>
        </Border>
        <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="1" CornerRadius="4" Padding="8,6"
                Visibility="{Binding IsExpanded, Converter={StaticResource BoolToVis}}">
            <StackPanel>
                <TextBlock Text="(Inline editor — fields vary by trigger type)" Foreground="{DynamicResource MutedBrush}" FontSize="11" />
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

- [ ] **Step 9: Build to verify the inline rule row compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 10: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml
git commit -m "feat(avatar-swap): migrate roulette editor and inline rule row to theme"
```

---

## Task 12: Visual smoke test in debug launcher

**Files:** No file changes. Verification only.

- [ ] **Step 1: Confirm a debug build is available**

Run:
```
Test-Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\bin\Debug\net10.0-windows\CrystalRelayTwitchOsc.exe"
```

Expected: `True` (or similar — the path may vary slightly by build configuration). If `False`, rebuild:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

- [ ] **Step 2: Launch the debug app**

Run:
```
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Expected: A debug instance of Crystal Relay opens. The window title should contain "DEBUG" so it can be told apart from a stable/test/beta build.

- [ ] **Step 3: Open the Avatar Swap manager**

In the running app, navigate to the Avatar Swap section and click the "Avatar Swap" button (per the main window). The Avatar Swap manager window opens.

- [ ] **Step 4: Verify Void Crystal theme rendering**

With the active theme set to `Void Crystal`:
- Cards render with the themed purple palette (no leftover hardcoded purple from the old XAML — the entire window should look like the rest of the app's themed surfaces).
- Empty cards show `🎭` + "Pick Avatar" centered in the hero area.
- Status stripe is green on enabled cards, gray on disabled cards.
- Rule-count pill appears top-right of cards that have any rules; absent otherwise.
- Hovering a card switches the border to the accent color (purple).
- Subtitle text is readable (MutedBrush, 10pt).
- Right editor panel is themed (PanelBrush background, MutedBrush for subtitles, themed button styles).
- "Power-up" button wraps to a new line if the right column is narrowed (resize the window to ~900px wide and verify).
- Inline rule rows in the right editor are themed (PanelBrush background, themed delete button).

- [ ] **Step 5: Verify theme switching**

Open the main app's settings/theme picker and switch to a different theme (e.g., `Custom`, `Dream Scape`, or `Baked`):
- All of the above still holds in the Avatar Swap manager — no leftover hardcoded purple.
- The status stripe, hover, and pill colors all follow the new theme.

- [ ] **Step 6: Verify the IsEnabled toggle**

With an Avatar Swap card selected in the editor, toggle its `IsEnabled` state. Confirm the status stripe on the corresponding card updates between green and gray.

- [ ] **Step 7: Close the app**

Close the debug instance. The `OnClosed` override should cleanly unsubscribe from `ThemeManager.ThemeChanged` without throwing.

- [ ] **Step 8: Commit any final fixes (only if needed)**

If any of the above checks failed, fix the corresponding XAML, rebuild, retest. Then:

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml
git commit -m "fix(avatar-swap): address visual smoke test findings"
```

If everything passed, no commit is needed for this task.

---

## Task 13: Run the localization audit and full test suite

**Files:** No file changes. Verification only.

- [ ] **Step 1: Run the full test suite**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```

Expected: All tests PASS (existing tests + the 5 new `HasAnyRules` tests from Task 1).

- [ ] **Step 2: Run the localization audit**

Run:
```powershell
& "E:\!!!Program to work on\Proper Crystal Relay\tools\github\Test-Crystal-Relay-PublicSafety.ps1"
```

(The localization audit is embedded in the public-safety preflight. Adjust the script name if the localization-only audit is a separate script. If neither exists, the build scripts' pre-flight gate will catch missing keys on the next test build.)

Expected: 0 missing keys across all 13 language files. No empty values.

- [ ] **Step 3: If the audit reports missing keys, fix them**

Open the language file(s) flagged by the audit and add the missing `Avatar Swap Card Pick Avatar` key with the appropriate translation. Re-run the audit. Repeat until clean.

- [ ] **Step 4: Commit any audit fixes (only if needed)**

```bash
git add VrcTwitchOscBridge/Resources/Localization/
git commit -m "fix(avatar-swap): complete Avatar Swap Card Pick Avatar translations"
```

---

## Task 14: Update `CHANGELOG.txt` and verify `AGENTS.md`

**Files:**
- Modify: `CHANGELOG.txt`.
- Read: `AGENTS.md` (verify only — no change expected).

- [ ] **Step 1: Read `AGENTS.md` and confirm the active build lane**

Look at the top of `AGENTS.md` for the `Active build lane` and `Active development build` lines. They should read:
```
- Active development build: v3.1.10
- Active build lane: beta1
```

If they do, no edit needed. If they differ, update them to match (this should be rare — confirm with the user before changing).

- [ ] **Step 2: Read the top of `CHANGELOG.txt` to find the existing `v3.1.10 beta 1` section (or the appropriate insertion point)**

If a `v3.1.10 beta 1` section already exists, add a new bullet to it. If not, add the new section at the top.

- [ ] **Step 3: Add the changelog entry**

Insert at the top of `CHANGELOG.txt` (above any existing `v3.1.10 beta` entries, or as a new section if none exist):

```
v3.1.10 beta 1
- Overhauled the Avatar Swap manager window to use the shared theme palette, so it now matches the rest of the app across every built-in theme (Void Crystal, Custom, Dream Scape, etc.).
- Added a status stripe, empty-state hint, rule-count pill, and hover state to each Avatar Swap card so enabled/disabled state, rule totals, and missing avatars are visible at a glance.
- Fixed the right editor's "Power-up" advanced trigger button so it wraps cleanly when the column is narrow instead of clipping at the edge.
- Bumped the inline rule row and the swap card subtitle font sizes from 9pt to 10pt for better readability.
```

(If the entry lives in a `.extra.json` rather than the main changelog, add the matching translation per the standard process.)

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.txt
git commit -m "docs(avatar-swap): add v3.1.10 beta 1 changelog entry for card visual overhaul"
```

---

## Verification Gates (final)

Before declaring done, confirm:

- [ ] All 14 tasks completed and committed.
- [ ] `dotnet build` of the app project succeeds with 0 errors.
- [ ] `dotnet test` of the test project shows all tests passing.
- [ ] Localization audit shows 0 missing keys.
- [ ] Visual smoke test (Task 12) passed in at least two themes.
- [ ] `AGENTS.md` `Active development build` reads `v3.1.10` and `Active build lane` reads `beta1`.
- [ ] `CHANGELOG.txt` has a `v3.1.10 beta 1` section describing the visual overhaul.

After all gates pass, the change is ready for a beta1 build:

```
powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Beta.ps1" -Version 3.1.10
```
