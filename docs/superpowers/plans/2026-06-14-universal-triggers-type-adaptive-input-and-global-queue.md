# Universal Triggers — Type-Adaptive Input + Global Queue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Universal Triggers editor's `Value` / `Reset to` input controls swap to match the action's `ValueKind` (Bool → CheckBox, Int/Float → numeric TextBox, String → plain TextBox), and add a single global `SemaphoreSlim(1,1)` so two Universal Trigger redeems with `AddToQueue=true` serialize across different triggers, not just within the same trigger.

**Architecture:** Three pure `IValueConverter`s handle the type adaptation at the XAML binding boundary — storage stays a string (`TargetValue` / `DefaultValue`), parsing happens in the converters using `CultureInfo.InvariantCulture`. The visibility swap reuses the existing `EnumToVisibilityConverter` in `Converters.cs`. A new `SemaphoreSlim(1,1)` `universalTriggerGlobalGate` wraps the existing per-trigger `SemaphoreSlim` in `BridgeCoordinator.ExecuteUniversalTriggerAsync`. The Fooma importer is untouched.

**Tech Stack:** C# .NET 10 WPF, xUnit (new test project), existing `loc:Translate` localization markup.

**Reference spec:** `docs/superpowers/specs/2026-06-14-universal-triggers-type-adaptive-input-and-global-queue-design.md`

---

## File structure

| File | Status | Purpose |
|---|---|---|
| `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs` | new | Three pure converters. |
| `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml` | edit | Swap input controls, add converters as `Window.Resources`, add column layout for Value/Reset. |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | edit | Add `universalTriggerGlobalGate` field, wrap per-trigger `WaitAsync` in it, dispose in stop path. |
| `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` | edit | Add `<Compile Include>` entry for the new converter file. |
| `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` | edit | Add `Universal Triggers Value On` → `On`. |
| `VrcTwitchOscBridge/Resources/Localization/de-DE.extra.json` | edit | Add German translation. |
| `VrcTwitchOscBridge/Resources/Localization/es-ES.extra.json` | edit | Add Spanish translation. |
| `VrcTwitchOscBridge/Resources/Localization/fr-FR.extra.json` | edit | Add French translation. |
| `VrcTwitchOscBridge/Resources/Localization/it-IT.extra.json` | edit | Add Italian translation. |
| `VrcTwitchOscBridge/Resources/Localization/ja-JP.extra.json` | edit | Add Japanese translation. |
| `VrcTwitchOscBridge/Resources/Localization/ko-KR.extra.json` | edit | Add Korean translation. |
| `VrcTwitchOscBridge/Resources/Localization/pl-PL.extra.json` | edit | Add Polish translation. |
| `VrcTwitchOscBridge/Resources/Localization/pt-BR.extra.json` | edit | Add Portuguese translation. |
| `VrcTwitchOscBridge/Resources/Localization/ru-RU.extra.json` | edit | Add Russian translation. |
| `VrcTwitchOscBridge/Resources/Localization/sv-SE.extra.json` | edit | Add Swedish translation. |
| `VrcTwitchOscBridge/Resources/Localization/th-TH.extra.json` | edit | Add Thai translation. |
| `VrcTwitchOscBridge/Resources/Localization/zh-CN.extra.json` | edit | Add Simplified Chinese translation. |
| `VrcTwitchOscBridge/Resources/Localization/zh-TW.extra.json` | edit | Add Traditional Chinese translation. |
| `VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj` | new | xUnit test project, `net10.0-windows`. |
| `VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs` | new | xUnit tests for the three converters. |

No model, no persistence, no Fooma importer, no Twitch reward sync, no theme system changes.

---

## Task 1: Create the test project

**Files:**
- Create: `VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj`

- [ ] **Step 1: Create the test project folder and csproj**

Create folder `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests`.

Write `VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj` with this content:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create an empty test file as a placeholder**

Write `VrcTwitchOscBridge.Tests/Converters/.gitkeep` (empty file) so the folder is tracked. (No test code yet — Task 2 adds the first test.)

- [ ] **Step 3: Verify the project restores**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet restore VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj
```

Expected: `Restore succeeded` with no errors. Warnings about transitive packages are OK.

- [ ] **Step 4: Verify the project builds empty**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet build VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj --no-restore
```

Expected: `Build succeeded` with 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj VrcTwitchOscBridge.Tests/Converters/.gitkeep && git commit -m "test: add VrcTwitchOscBridge.Tests xUnit project (empty)"
```

---

## Task 2: BoolConverter — failing test

**Files:**
- Test: `VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs`

- [ ] **Step 1: Create the test file with the BoolConverter test class**

Write `VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs`:

```csharp
using System.Globalization;
using Xunit;
using VrcTwitchOscBridge;

namespace VrcTwitchOscBridge.Tests.Converters;

public sealed class UniversalTriggerBoolConverterTests
{
    [Theory]
    [InlineData("True", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("False", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    public void Convert_StringToBool_ReturnsExpected(string? input, bool expected)
    {
        var converter = new UniversalTriggerBoolConverter();
        var result = converter.Convert(input, typeof(bool), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    [InlineData(null, "False")]
    public void ConvertBack_BoolToString_ReturnsCanonicalForm(bool? input, string expected)
    {
        var converter = new UniversalTriggerBoolConverter();
        var result = converter.ConvertBack(input, typeof(string), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet test VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj --filter "FullyQualifiedName~UniversalTriggerBoolConverterTests"
```

Expected: Build fails with `error CS0246: The type or namespace name 'UniversalTriggerBoolConverter' could not be found`. This is the failing-test step.

- [ ] **Step 3: Commit the failing test**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs && git commit -m "test: add UniversalTriggerBoolConverter tests (failing)"
```

---

## Task 3: BoolConverter — implementation

**Files:**
- Create: `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add `<Compile Include>`)

- [ ] **Step 1: Create the new converter file with BoolConverter**

Write `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs`:

```csharp
using System.Globalization;
using System.Windows.Data;

namespace VrcTwitchOscBridge;

[ValueConversion(typeof(string), typeof(bool))]
public sealed class UniversalTriggerBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return false;
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (bool.TryParse(text, out var parsed))
        {
            return parsed;
        }

        if (text == "1")
        {
            return true;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "True" : "False";
    }
}
```

- [ ] **Step 2: Register the new file in the main csproj**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`. Find the existing `<Compile Include="Converters.cs" />` entry (around line 97). Add this line directly after it:

```xml
    <Compile Include="Converters\UniversalTriggerValueConverters.cs" />
```

The project uses `EnableDefaultCompileItems=false` per `AGENTS.md`, so new `.cs` files are not picked up automatically.

- [ ] **Step 3: Run the test to verify it passes**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet test VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj --filter "FullyQualifiedName~UniversalTriggerBoolConverterTests"
```

Expected: All 13 test cases pass. Output includes `Passed: 13`.

- [ ] **Step 4: Verify the main app still builds**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet build VrcTwitchOscBridge/VrcTwitchOscBridge.csproj --no-restore
```

Expected: `Build succeeded`. The new converter is included via the new `<Compile Include>` entry. No new warnings about unused converters (WPF resolves them lazily through `StaticResource`).

- [ ] **Step 5: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj && git commit -m "feat(universal-triggers): add UniversalTriggerBoolConverter + register in csproj"
```

---

## Task 4: IntConverter — failing test

**Files:**
- Test: `VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs`

- [ ] **Step 1: Append the IntConverter test class to the test file**

Open `VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs`. Append the following class after the closing `}` of `UniversalTriggerBoolConverterTests`:

```csharp
public sealed class UniversalTriggerIntConverterTests
{
    [Theory]
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("-1", "-1")]
    [InlineData("2147483647", "2147483647")]
    [InlineData("-2147483648", "-2147483648")]
    [InlineData("1.0", "1")]
    [InlineData("abc", "abc")]
    [InlineData("1.5", "1.5")]
    public void Convert_StringToString_FormatsInt(string? input, string expected)
    {
        var converter = new UniversalTriggerIntConverter();
        var result = converter.Convert(input, typeof(string), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("-1", -1)]
    [InlineData("2147483647", 2147483647)]
    [InlineData("abc", 0)]
    [InlineData("1.5", 0)]
    public void ConvertBack_StringToInt_ReturnsExpected(string? input, int expected)
    {
        var converter = new UniversalTriggerIntConverter();
        var result = converter.ConvertBack(input, typeof(int), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertBack_InvalidInput_DoesNotThrow()
    {
        var converter = new UniversalTriggerIntConverter();
        var result = converter.ConvertBack("not a number", typeof(int), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(0, result);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet test VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj --filter "FullyQualifiedName~UniversalTriggerIntConverterTests"
```

Expected: Build fails with `error CS0246: The type or namespace name 'UniversalTriggerIntConverter' could not be found`. This is the failing-test step.

- [ ] **Step 3: Commit the failing test**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs && git commit -m "test: add UniversalTriggerIntConverter tests (failing)"
```

---

## Task 5: IntConverter — implementation

**Files:**
- Modify: `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs`

- [ ] **Step 1: Append the IntConverter class**

Open `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs`. Append this class after the closing `}` of `UniversalTriggerBoolConverter`:

```csharp

[ValueConversion(typeof(string), typeof(string))]
public sealed class UniversalTriggerIntConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return "0";
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return "0";
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue.ToString(CultureInfo.InvariantCulture);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
            && doubleValue == Math.Truncate(doubleValue)
            && doubleValue >= int.MinValue
            && doubleValue <= int.MaxValue)
        {
            return ((int)doubleValue).ToString(CultureInfo.InvariantCulture);
        }

        return text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return 0;
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet test VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj --filter "FullyQualifiedName~UniversalTriggerIntConverterTests"
```

Expected: All test cases pass. Output includes `Passed: 19` (10 Convert + 8 ConvertBack + 1 ConvertBack_InvalidInput_DoesNotThrow).

- [ ] **Step 3: Verify Bool tests still pass**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet test VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj --filter "FullyQualifiedName~UniversalTriggerValueConverters"
```

Expected: All Bool + Int tests pass. No regressions.

- [ ] **Step 4: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs && git commit -m "feat(universal-triggers): add UniversalTriggerIntConverter"
```

---

## Task 6: FloatConverter — failing test

**Files:**
- Test: `VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs`

- [ ] **Step 1: Append the FloatConverter test class to the test file**

Open `VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs`. Append the following class after the closing `}` of `UniversalTriggerIntConverterTests`:

```csharp
public sealed class UniversalTriggerFloatConverterTests
{
    [Theory]
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("-1", "-1")]
    [InlineData("1.5", "1.5")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("0.0001", "0.0001")]
    [InlineData("1.0", "1")]
    [InlineData("abc", "abc")]
    public void Convert_StringToString_FormatsFloat(string? input, string expected)
    {
        var converter = new UniversalTriggerFloatConverter();
        var result = converter.Convert(input, typeof(string), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData("", 0.0)]
    [InlineData("0", 0.0)]
    [InlineData("1.5", 1.5)]
    [InlineData("-1.5", -1.5)]
    [InlineData("0.0001", 0.0001)]
    [InlineData("abc", 0.0)]
    public void ConvertBack_StringToDouble_ReturnsExpected(string? input, double expected)
    {
        var converter = new UniversalTriggerFloatConverter();
        var result = converter.ConvertBack(input, typeof(double), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertBack_InvalidInput_DoesNotThrow()
    {
        var converter = new UniversalTriggerFloatConverter();
        var result = converter.ConvertBack("not a number", typeof(double), parameter: null, CultureInfo.InvariantCulture);
        Assert.Equal(0.0, result);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet test VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj --filter "FullyQualifiedName~UniversalTriggerFloatConverterTests"
```

Expected: Build fails with `error CS0246: The type or namespace name 'UniversalTriggerFloatConverter' could not be found`. This is the failing-test step.

- [ ] **Step 3: Commit the failing test**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs && git commit -m "test: add UniversalTriggerFloatConverter tests (failing)"
```

---

## Task 7: FloatConverter — implementation

**Files:**
- Modify: `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs`

- [ ] **Step 1: Append the FloatConverter class**

Open `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs`. Append this class after the closing `}` of `UniversalTriggerIntConverter`:

```csharp

[ValueConversion(typeof(string), typeof(string))]
public sealed class UniversalTriggerFloatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return "0";
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return "0";
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            if (doubleValue == Math.Truncate(doubleValue)
                && !double.IsInfinity(doubleValue)
                && doubleValue >= -1e15
                && doubleValue <= 1e15)
            {
                return ((long)doubleValue).ToString(CultureInfo.InvariantCulture);
            }

            return doubleValue.ToString("R", CultureInfo.InvariantCulture);
        }

        return text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return 0.0;
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return 0.0;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0.0;
    }
}
```

- [ ] **Step 2: Run the Float tests to verify they pass**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet test VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj --filter "FullyQualifiedName~UniversalTriggerFloatConverterTests"
```

Expected: All test cases pass.

- [ ] **Step 3: Run all converter tests to verify no regressions**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet test VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj --filter "FullyQualifiedName~UniversalTriggerValueConverters"
```

Expected: All Bool + Int + Float tests pass. Total `Passed: 49` (13 Bool + 19 Int + 17 Float).

- [ ] **Step 4: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs && git commit -m "feat(universal-triggers): add UniversalTriggerFloatConverter"
```

---

## Task 8: Localization — add `On` key in en-US

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`

- [ ] **Step 1: Find the right place to insert the new key**

Open `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`. Find the existing `"Universal Triggers": "Universal Triggers",` line (line 121). Insert a new line right after it:

```json
  "Universal Triggers Value On": "On",
```

- [ ] **Step 2: Verify the JSON is still valid**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && powershell -Command "Get-Content 'VrcTwitchOscBridge/Resources/Localization/en-US.extra.json' | ConvertFrom-Json | Out-Null; Write-Host 'JSON valid'"
```

Expected: `JSON valid`. If the file is malformed, this will throw and show the parse error.

- [ ] **Step 3: Commit en-US only**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge/Resources/Localization/en-US.extra.json && git commit -m "feat(localization): add 'Universal Triggers Value On' key in en-US"
```

---

## Task 9: Localization — add the key in 12 other languages

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/{de-DE,es-ES,fr-FR,it-IT,ja-JP,ko-KR,pl-PL,pt-BR,ru-RU,sv-SE,th-TH,zh-CN,zh-TW}.extra.json`

- [ ] **Step 1: Add the key to each language file**

For each language file, find the line `"Universal Triggers": "Universal Triggers",` and insert the new key right after it. Use these translations (informal register per `AGENTS.md`):

| File | Translation |
|---|---|
| `de-DE.extra.json` | `"Universal Triggers Value On": "An",` |
| `es-ES.extra.json` | `"Universal Triggers Value On": "Activado",` |
| `fr-FR.extra.json` | `"Universal Triggers Value On": "Activé",` |
| `it-IT.extra.json` | `"Universal Triggers Value On": "On",` |
| `ja-JP.extra.json` | `"Universal Triggers Value On": "オン",` |
| `ko-KR.extra.json` | `"Universal Triggers Value On": "켜기",` |
| `pl-PL.extra.json` | `"Universal Triggers Value On": "Włączone",` |
| `pt-BR.extra.json` | `"Universal Triggers Value On": "Ligado",` |
| `ru-RU.extra.json` | `"Universal Triggers Value On": "Вкл",` |
| `sv-SE.extra.json` | `"Universal Triggers Value On": "På",` |
| `th-TH.extra.json` | `"Universal Triggers Value On": "เปิด",` |
| `zh-CN.extra.json` | `"Universal Triggers Value On": "开",` |
| `zh-TW.extra.json` | `"Universal Triggers Value On": "開",` |

- [ ] **Step 2: Verify all 14 files are still valid JSON**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && powershell -Command "Get-ChildItem 'VrcTwitchOscBridge/Resources/Localization/*.extra.json' | ForEach-Object { try { $_ | Get-Content -Raw | ConvertFrom-Json | Out-Null; Write-Host \"$($_.Name): valid\" } catch { Write-Host \"$($_.Name): INVALID - $_\" -ForegroundColor Red } }"
```

Expected: All 14 files show `valid`. No `INVALID` lines.

- [ ] **Step 3: Commit all 13 non-English localization files**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge/Resources/Localization/de-DE.extra.json VrcTwitchOscBridge/Resources/Localization/es-ES.extra.json VrcTwitchOscBridge/Resources/Localization/fr-FR.extra.json VrcTwitchOscBridge/Resources/Localization/it-IT.extra.json VrcTwitchOscBridge/Resources/Localization/ja-JP.extra.json VrcTwitchOscBridge/Resources/Localization/ko-KR.extra.json VrcTwitchOscBridge/Resources/Localization/pl-PL.extra.json VrcTwitchOscBridge/Resources/Localization/pt-BR.extra.json VrcTwitchOscBridge/Resources/Localization/ru-RU.extra.json VrcTwitchOscBridge/Resources/Localization/sv-SE.extra.json VrcTwitchOscBridge/Resources/Localization/th-TH.extra.json VrcTwitchOscBridge/Resources/Localization/zh-CN.extra.json VrcTwitchOscBridge/Resources/Localization/zh-TW.extra.json && git commit -m "feat(localization): add 'Universal Triggers Value On' key in 13 non-English locales"
```

---

## Task 10: XAML — add converter resources and rewrite the action row

**Files:**
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`

- [ ] **Step 1: Read the current `<Window>` opening tag and the OSC action row**

The current `<Window>` tag is at line 1 of the file. The OSC action row `UniformGrid` (which holds the `ValueKind` ComboBox, `TargetValue` TextBox, and `DefaultValue` TextBox) is at lines 1563-1569. The action row's outer `Grid.RowDefinitions` are at lines 1547-1552.

- [ ] **Step 2: Add converter resources to `Window.Resources`**

Open `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`. Find the `<Window ...>` opening tag. Right after the opening tag, add a `<Window.Resources>` block that registers the three converters. The tag will look like:

```xml
<Window ... >
    <Window.Resources>
        <converters:UniversalTriggerBoolConverter x:Key="UniversalTriggerBoolConverter" />
        <converters:UniversalTriggerIntConverter x:Key="UniversalTriggerIntConverter" />
        <converters:UniversalTriggerFloatConverter x:Key="UniversalTriggerFloatConverter" />
    </Window.Resources>
```

Then find the top-level `xmlns:` namespace declarations on the `<Window>` (right above the `>` of the opening tag). Add this namespace if it is not already present:

```xml
xmlns:converters="clr-namespace:VrcTwitchOscBridge"
```

(`Converters.cs` and the new `UniversalTriggerValueConverters.cs` both live in the `VrcTwitchOscBridge` namespace, so this single namespace is enough.)

- [ ] **Step 3: Rewrite the OSC action row's `UniformGrid`**

Find the existing `UniformGrid` block at lines 1563-1569:

```xml
<UniformGrid Grid.Row="1" Columns="3" Margin="0,4,0,0">
    <ComboBox SelectedItem="{Binding ValueKind, Mode=TwoWay}"
              ItemsSource="{Binding DataContext.UniversalTriggerValueKinds, RelativeSource={RelativeSource AncestorType=Window}}"
              Margin="0,0,4,0" />
    <TextBox Text="{Binding TargetValue, UpdateSourceTrigger=PropertyChanged}" Margin="2,0" />
    <TextBox Text="{Binding DefaultValue, UpdateSourceTrigger=PropertyChanged}" Margin="4,0,0,0" />
</UniformGrid>
```

Replace it with the following `Grid` block:

```xml
<Grid Grid.Row="1" Margin="0,4,0,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="140" />
        <ColumnDefinition Width="*" MinWidth="80" />
        <ColumnDefinition Width="*" MinWidth="120" />
    </Grid.ColumnDefinitions>
    <ComboBox Grid.Column="0"
              SelectedItem="{Binding ValueKind, Mode=TwoWay}"
              ItemsSource="{Binding DataContext.UniversalTriggerValueKinds, RelativeSource={RelativeSource AncestorType=Window}}"
              Margin="0,0,4,0" />
    <Grid Grid.Column="1">
        <CheckBox VerticalAlignment="Center"
                  Margin="4,0"
                  Content="{loc:Translate 'Universal Triggers Value On'}"
                  IsChecked="{Binding TargetValue, Converter={StaticResource UniversalTriggerBoolConverter}, Mode=TwoWay}"
                  Visibility="{Binding ValueKind, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=Bool}" />
        <TextBox Margin="2,0"
                 Text="{Binding TargetValue, Converter={StaticResource UniversalTriggerIntConverter}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                 Visibility="{Binding ValueKind, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=Int}" />
        <TextBox Margin="2,0"
                 Text="{Binding TargetValue, Converter={StaticResource UniversalTriggerFloatConverter}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                 Visibility="{Binding ValueKind, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=Float}" />
        <TextBox Margin="2,0"
                 Text="{Binding TargetValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                 Visibility="{Binding ValueKind, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=String}" />
    </Grid>
    <Grid Grid.Column="2">
        <CheckBox VerticalAlignment="Center"
                  Margin="4,0"
                  Content="{loc:Translate 'Universal Triggers Value On'}"
                  IsChecked="{Binding DefaultValue, Converter={StaticResource UniversalTriggerBoolConverter}, Mode=TwoWay}"
                  Visibility="{Binding ValueKind, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=Bool}" />
        <TextBox Margin="4,0,0,0"
                 Text="{Binding DefaultValue, Converter={StaticResource UniversalTriggerIntConverter}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                 Visibility="{Binding ValueKind, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=Int}" />
        <TextBox Margin="4,0,0,0"
                 Text="{Binding DefaultValue, Converter={StaticResource UniversalTriggerFloatConverter}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                 Visibility="{Binding ValueKind, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=Float}" />
        <TextBox Margin="4,0,0,0"
                 Text="{Binding DefaultValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                 Visibility="{Binding ValueKind, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=String}" />
    </Grid>
</Grid>
```

This places the four `TargetValue` controls in column 1, the four `DefaultValue` controls in column 2, and the `ValueKind` ComboBox in column 0. Each input control's `Visibility` is data-triggered on `ValueKind` via the existing `EnumToVisibilityConverter`.

- [ ] **Step 4: Build the main app to verify the XAML compiles**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet build VrcTwitchOscBridge/VrcTwitchOscBridge.csproj --no-restore
```

Expected: `Build succeeded`. The converters resolve via the new namespace import. The `EnumToVisibilityConverter` is already in `Converters.cs` and is part of the `VrcTwitchOscBridge` namespace.

- [ ] **Step 5: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml && git commit -m "feat(universal-triggers): swap value input to type-adaptive control"
```

---

## Task 11: BridgeCoordinator — add global queue gate

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Add the new `SemaphoreSlim` field**

Open `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`. Find the existing field declaration at line 179:

```csharp
private readonly Dictionary<Guid, SemaphoreSlim> universalTriggerQueueGates = [];
```

Add this line directly after it:

```csharp
private readonly SemaphoreSlim universalTriggerGlobalGate = new(1, 1);
```

- [ ] **Step 2: Wrap the per-trigger `WaitAsync` in the global `WaitAsync`**

Find the `ExecuteUniversalTriggerAsync` method (starts at line 3910). Find the `shouldQueue` block at lines 3924-3937. Replace the current:

```csharp
if (shouldQueue)
{
    var gate = GetUniversalTriggerQueueGate(trigger.Id);
    await gate.WaitAsync(cancellationToken);
    try
    {
        await ExecuteUniversalActionsAsync(trigger, actions, cancellationToken);
    }
    finally
    {
        gate.Release();
    }
}
```

with:

```csharp
if (shouldQueue)
{
    await universalTriggerGlobalGate.WaitAsync(cancellationToken);
    try
    {
        var gate = GetUniversalTriggerQueueGate(trigger.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await ExecuteUniversalActionsAsync(trigger, actions, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
    finally
    {
        universalTriggerGlobalGate.Release();
    }
}
```

The global gate is acquired first and released last. The per-trigger gate logic is unchanged.

- [ ] **Step 3: Dispose the global gate in the existing stop path**

Find the per-trigger gate dispose loop at line 17344:

```csharp
foreach (var universalQueueGate in universalQueueGates)
{
    universalQueueGate.Dispose();
}
```

Add this line directly after the closing `}` of the `foreach` (and before the `supporterGrowthCancellation` loop):

```csharp
universalTriggerGlobalGate.Dispose();
```

- [ ] **Step 4: Build the main app to verify the change compiles**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet build VrcTwitchOscBridge/VrcTwitchOscBridge.csproj --no-restore
```

Expected: `Build succeeded`. No new warnings.

- [ ] **Step 5: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs && git commit -m "feat(universal-triggers): add global SemaphoreSlim gate for cross-trigger queue"
```

---

## Task 12: Full verification

**Files:** none (read-only)

- [ ] **Step 1: Run all converter tests**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet test VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj
```

Expected: All converter tests pass. `Passed: 49`, `Failed: 0`.

- [ ] **Step 2: Run the full main app build**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet build VrcTwitchOscBridge/VrcTwitchOscBridge.csproj --no-restore
```

Expected: `Build succeeded`. No new warnings introduced by this change.

- [ ] **Step 3: Run the localization audit**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && dotnet run --project LocalizationAudit
```

Expected: Audit completes with `0` untranslated keys. Specifically, `Universal Triggers Value On` is present in all 14 `*.extra.json` files (en-US + 13 non-English locales).

- [ ] **Step 4: Run the dependency vulnerability scan**

Run:

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay" && powershell -ExecutionPolicy Bypass -File "Check-Crystal-Relay-Dependencies.ps1"
```

Expected: Scan completes. The new test project's `xunit` and `Microsoft.NET.Test.Sdk` packages should appear; no critical/high vulnerabilities (the project has a hard rule on these per the AGENTS.md build script).

- [ ] **Step 5: Manual smoke test (optional but recommended)**

Run the debug launcher to launch the rebuilt app:

```bash
"E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Expected: app launches with a `- DEBUG` suffix in the title bar. Open the Universal Triggers tab. Create a new trigger, add an OSC action, change the `ValueKind` dropdown — verify the `Value` input swaps to a `CheckBox` for `Bool`, a numeric `TextBox` for `Int`/`Float`, and a plain `TextBox` for `String`. Same for `Reset to`. Save the trigger. Redeem it twice in quick succession with `AddToQueue=true` and verify the WriteLog timestamps show serialization.

---

## Self-review (already completed by planner)

- Spec coverage: Bool converter §5.1 → Tasks 2-3. Int converter §5.2 → Tasks 4-5. Float converter §5.3 → Tasks 6-7. Visibility swap §6 → Task 10. Localization §11 → Tasks 8-9. Global queue §7 → Task 11. Acceptance criteria §12 → Task 12.
- Placeholder scan: no TBD / TODO / "implement later" strings. All steps show exact code.
- Type consistency: `UniversalTriggerBoolConverter` / `UniversalTriggerIntConverter` / `UniversalTriggerFloatConverter` names match across Tasks 2-7. The `ValueKind`-to-Visibility binding parameter (`"Bool"`, `"Int"`, `"Float"`, `"String"`) matches the enum members of `UniversalTriggerValueKind`. The `EnumToVisibilityConverter` is reused (not duplicated).
- Out-of-scope files are not touched: `Models/UniversalTrigger*`, `Services/FoomaInteractionConfigImporter.cs`, `Services/UniversalTriggerFusionService.cs`, `Services/SettingsStore.cs`, `Services/VrChatOscClient.cs`, `Services/BridgeRuntimeConfiguration.cs`, `ThemeManager.cs`, any other redeem library, the Twitch reward sync code, the chatbox, the about page, the persistence DTO, the migrator chain.
