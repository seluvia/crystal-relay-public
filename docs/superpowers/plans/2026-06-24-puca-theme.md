# Púca Theme Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new built-in WPF visual theme "Púca" (cyan/purple/pastel-pink, dark mystical night mood, Pokémon-style fused background) to Crystal Relay.

**Architecture:** Follow the established built-in theme pattern exactly — add an `AppTheme` enum member, a `ThemePaletteFactory` switch arm with all 44 brush keys, a vector `ThemeBackgrounds` XAML file, a `ThemeOption` entry + `IsPucaThemeSelected` property, per-theme branches in the 6 legacy windows that still hardcode colors, and an `AGENTS.md` housekeeping entry.

**Tech Stack:** C# / WPF / XAML on .NET 10 (`net10.0-windows`)

**Spec:** `docs/superpowers/specs/2026-06-24-puca-theme-design.md`

---

### Task 1: Add the `Puca` enum member

**Files:**
- Modify: `VrcTwitchOscBridge\Models\AppTheme.cs:23`

- [ ] **Step 1: Add the enum member at the end**

Edit `VrcTwitchOscBridge\Models\AppTheme.cs`. Change line 23 from:

```csharp
    SquishyFoxPlush
}
```

to:

```csharp
    SquishyFoxPlush,
    Puca
}
```

This appends `Puca` at the end to preserve persisted integer values for all existing themes (per AGENTS.md versioning/persistence rules).

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED (the enum member is unused until Task 2 wires it up, but it compiles fine).

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/Models/AppTheme.cs
git commit -m "Add Puca theme enum member"
```

---

### Task 2: Add the Púca palette factory arm

**Files:**
- Modify: `VrcTwitchOscBridge\Services\ThemeManager.cs:1013`

- [ ] **Step 1: Add the `AppTheme.Puca =>` switch arm**

In `VrcTwitchOscBridge\Services\ThemeManager.cs`, inside the `ThemePaletteFactory.CreatePalette` switch expression, the `SquishyFoxPlush` arm ends at line 1013 with `("TitleBarClosePressedBrush", "#933722")),` and the `_ =>` default arm begins at line 1014. Insert a new arm between them.

Change this exact text (lines 1013-1014):

```csharp
                ("TitleBarClosePressedBrush", "#933722")),
            _ => CreateBuiltInPalette(
```

to:

```csharp
                ("TitleBarClosePressedBrush", "#933722")),
            AppTheme.Puca => CreateBuiltInPalette(
                AppTheme.Puca,
                "Verdana",
                "Cambria",
                24d,
                ("WindowBackgroundBrush", "#0C0716"),
                ("PanelBrush", "#E6140C24"),
                ("PanelSecondaryBrush", "#D910081A"),
                ("PanelHighlightBrush", "#D91C1238"),
                ("BorderBrush", "#3A2868"),
                ("AccentBrush", "#22D3EE"),
                ("TextBrush", "#E8DEF8"),
                ("MutedBrush", "#A896C8"),
                ("InputBrush", "#E6080410"),
                ("InputBorderBrush", "#4A2D8A"),
                ("ComboSurfaceBrush", "#1A1030"),
                ("ComboTextBrush", "#E8DEF8"),
                ("ComboHighlightBrush", "#22D3EE"),
                ("SecondaryButtonBrush", "#2AF5B8E0"),
                ("SecondaryButtonBorderBrush", "#F5B8E0"),
                ("SectionActiveBrush", "#22D3EE"),
                ("RuleCardBrush", "#E6140C24"),
                ("RuleCardSelectedBrush", "#2622D3EE"),
                ("AccentDimBrush", "#1A7E92"),
                ("RuleCardHoverBrush", "#2A1A3450"),
                ("StatusChipBrush", "#2CA78BFA"),
                ("NestedPanelBrush", "#D910081A"),
                ("DangerBrush", "#C0395F"),
                ("DangerBorderBrush", "#8C2648"),
                ("WarnBrush", "#7A5A2A"),
                ("WarnBorderBrush", "#A8884A"),
                ("WarnTextBrush", "#F0D878"),
                ("HighlightBorderBrush", "#22D3EE"),
                ("PopupBorderBrush", "#3A2868"),
                ("ComboDropButtonBrush", "#2A1A3A"),
                ("ComboDropButtonHoverBrush", "#3A2868"),
                ("ComboDropButtonPressedBrush", "#4A3878"),
                ("ScrollTrackBrush", "#1A1028"),
                ("ScrollThumbBrush", "#22D3EE"),
                ("ScrollThumbHoverBrush", "#7DEFFF"),
                ("ScrollThumbPressedBrush", "#5DE8F5"),
                ("TitleBarBrush", "#1A0E2E"),
                ("TitleBarTextBrush", "#7DEFFF"),
                ("TitleBarSubTextBrush", "#F5B8E0"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#3A2868"),
                ("TitleBarButtonPressedBrush", "#4A3878"),
                ("TitleBarCloseHoverBrush", "#C0395F"),
                ("TitleBarClosePressedBrush", "#8C2648")),
            _ => CreateBuiltInPalette(
```

Note: `"Verdana"` is the body font (first), `"Cambria"` is the heading font (second), matching the `CreateBuiltInPalette(AppTheme, string bodyFontFamily, string headingFontFamily, double homeTitleFontSize, params ...)` signature at `ThemeManager.cs:1155`.

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/Services/ThemeManager.cs
git commit -m "Add Puca palette to ThemePaletteFactory"
```

---

### Task 3: Create the Púca background XAML

**Files:**
- Create: `VrcTwitchOscBridge\ThemeBackgrounds\PucaThemeBackground.xaml`

This file is auto-included by the wildcard `<Page Include="ThemeBackgrounds\*.xaml" />` at `VrcTwitchOscBridge.csproj:40` — no csproj edit needed.

- [ ] **Step 1: Create the background XAML file**

Create `VrcTwitchOscBridge\ThemeBackgrounds\PucaThemeBackground.xaml` with this exact content:

```xml
<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid.Background>
        <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
            <GradientStop Color="#0C0716" Offset="0" />
            <GradientStop Color="#161028" Offset="0.42" />
            <GradientStop Color="#100A20" Offset="0.78" />
            <GradientStop Color="#080410" Offset="1" />
        </LinearGradientBrush>
    </Grid.Background>
    <Canvas ClipToBounds="True">

        <!-- Aurora ribbon wisps -->
        <Path Data="M-20,90 Q120,40 240,100 T440,80" Stroke="#3F22D3EE" StrokeThickness="40" Fill="None" Opacity="0.55" />
        <Path Data="M-20,300 Q140,260 280,310 T440,290" Stroke="#3FA78BFA" StrokeThickness="46" Fill="None" Opacity="0.55" />
        <Path Data="M-20,350 Q160,330 320,370 T440,350" Stroke="#3FF5B8E0" StrokeThickness="34" Fill="None" Opacity="0.50" />
        <Path Data="M-20,190 Q100,160 200,195 T420,180" Stroke="#3F22D3EE" StrokeThickness="22" Fill="None" Opacity="0.35" />

        <!-- Central nebula glow -->
        <Ellipse Width="300" Height="300" Canvas.Left="650" Canvas.Top="330" Fill="#336C28C8" Opacity="0.45" />

        <!-- Orbiting small circles (outside the main circle) -->
        <!-- top-left pair -->
        <Ellipse Width="50" Height="50" Canvas.Left="60" Canvas.Top="80" Stroke="#9922D3EE" StrokeThickness="1.3" Fill="None" />
        <Ellipse Width="30" Height="30" Canvas.Left="82" Canvas.Top="100" Stroke="#B3A78BFA" StrokeThickness="1" StrokeDashArray="2 2" Fill="None" />
        <!-- bottom-right pair -->
        <Ellipse Width="44" Height="44" Canvas.Left="1380" Canvas.Top="620" Stroke="#99F5B8E0" StrokeThickness="1.3" Fill="None" />
        <Ellipse Width="26" Height="26" Canvas.Left="1390" Canvas.Top="636" Stroke="#9922D3EE" StrokeThickness="1" StrokeDashArray="2 2" Fill="None" />
        <!-- bottom-left single -->
        <Ellipse Width="28" Height="28" Canvas.Left="80" Canvas.Top="680" Stroke="#8CA78BFA" StrokeThickness="1" Fill="None" />
        <!-- top-right single -->
        <Ellipse Width="32" Height="32" Canvas.Left="1450" Canvas.Top="70" Stroke="#8022D3EE" StrokeThickness="1" Fill="None" />

        <!-- Central arcane magic circle (3 concentric rings) -->
        <Ellipse Width="220" Height="220" Canvas.Left="690" Canvas.Top="370" Stroke="#8C22D3EE" StrokeThickness="1.5" Fill="None" />
        <Ellipse Width="176" Height="176" Canvas.Left="712" Canvas.Top="392" Stroke="#B3A78BFA" StrokeThickness="1.5" StrokeDashArray="4 4" Fill="None" />
        <Ellipse Width="120" Height="120" Canvas.Left="740" Canvas.Top="420" Stroke="#99F5B8E0" StrokeThickness="1" Fill="None" />

        <!-- Rune ticks (8 cardinal/diagonal marks around outer ring) -->
        <Canvas>
            <Line X1="800" Y1="366" X2="800" Y2="380" Stroke="#B322D3EE" StrokeThickness="1.6" />
            <Line X1="800" Y1="594" X2="800" Y2="608" Stroke="#B322D3EE" StrokeThickness="1.6" />
            <Line X1="686" Y1="480" X2="700" Y2="480" Stroke="#B322D3EE" StrokeThickness="1.6" />
            <Line X1="900" Y1="480" X2="914" Y2="480" Stroke="#B322D3EE" StrokeThickness="1.6" />
            <Line X1="719" Y1="399" X2="729" Y2="409" Stroke="#B322D3EE" StrokeThickness="1.6" />
            <Line X1="871" Y1="551" X2="881" Y2="561" Stroke="#B322D3EE" StrokeThickness="1.6" />
            <Line X1="719" Y1="561" X2="729" Y2="551" Stroke="#B322D3EE" StrokeThickness="1.6" />
            <Line X1="871" Y1="409" X2="881" Y2="399" Stroke="#B322D3EE" StrokeThickness="1.6" />
        </Canvas>

        <!-- Crescent moon glow halo -->
        <Ellipse Width="130" Height="130" Canvas.Left="735" Canvas.Top="415" Fill="#2622D3EE" Opacity="0.6" />

        <!-- Crescent moon (centered, larger) -->
        <Path Data="M 770,480 a 26,26 0 1 0 26,-26 a 19,19 0 1 1 -26,26 Z"
              Fill="#EB22D3EE"
              Stroke="#B37DEFFF"
              StrokeThickness="1.5" />

        <!-- Pink spark (off the moon, upper area) -->
        <Ellipse Width="40" Height="40" Canvas.Left="540" Canvas.Top="150" Fill="#26F5B8E0" Opacity="0.5" />
        <Ellipse Width="9" Height="9" Canvas.Left="555" Canvas.Top="165" Fill="#FFF5B8E0" />

        <!-- Constellation lines -->
        <Polyline Points="40,70 95,55 150,80 195,62" Stroke="#597DEFFF" StrokeThickness="0.8" Fill="None" />
        <Polyline Points="285,690 340,675 378,705" Stroke="#597DEFFF" StrokeThickness="0.8" Fill="None" />
        <Polyline Points="60,700 115,725 158,708" Stroke="#597DEFFF" StrokeThickness="0.8" Fill="None" />

        <!-- Starfield -->
        <Ellipse Width="4" Height="4" Canvas.Left="40" Canvas.Top="70" Fill="#7DEFFF" />
        <Ellipse Width="3" Height="3" Canvas.Left="95" Canvas.Top="55" Fill="#FFFFFF" />
        <Ellipse Width="4" Height="4" Canvas.Left="150" Canvas.Top="80" Fill="#7DEFFF" />
        <Ellipse Width="3" Height="3" Canvas.Left="195" Canvas.Top="62" Fill="#A78BFA" />
        <Ellipse Width="4" Height="4" Canvas.Left="285" Canvas.Top="690" Fill="#F5B8E0" />
        <Ellipse Width="3" Height="3" Canvas.Left="340" Canvas.Top="675" Fill="#7DEFFF" />
        <Ellipse Width="3" Height="3" Canvas.Left="378" Canvas.Top="705" Fill="#A78BFA" />
        <Ellipse Width="4" Height="4" Canvas.Left="60" Canvas.Top="700" Fill="#7DEFFF" />
        <Ellipse Width="3" Height="3" Canvas.Left="115" Canvas.Top="725" Fill="#F5B8E0" />
        <Ellipse Width="3" Height="3" Canvas.Left="1395" Canvas.Top="55" Fill="#7DEFFF" />
        <Ellipse Width="3" Height="3" Canvas.Left="1340" Canvas.Top="120" Fill="#FFFFFF" />
        <Ellipse Width="4" Height="4" Canvas.Left="50" Canvas.Top="280" Fill="#F5B8E0" />
        <Ellipse Width="3" Height="3" Canvas.Left="1430" Canvas.Top="340" Fill="#A78BFA" />

    </Canvas>
</Grid>
```

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED (the wildcard csproj include picks up the new XAML automatically).

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/ThemeBackgrounds/PucaThemeBackground.xaml
git commit -m "Add Puca theme background XAML"
```

---

### Task 4: Register the Púca background in MainWindow

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml.cs:586`

- [ ] **Step 1: Add the `LoadThemeBackground` switch arm**

In `VrcTwitchOscBridge\MainWindow.xaml.cs`, the `LoadThemeBackground` method has a switch. The `SquishyFoxPlush` arm is at line 586 and the `_ =>` fallback is at line 587. Insert a new arm between them.

Change this exact text (lines 586-587):

```csharp
            AppTheme.SquishyFoxPlush => "ThemeBackgrounds/SquishyFoxPlushThemeBackground.xaml",
            _ => "ThemeBackgrounds/VoidCrystalThemeBackground.xaml"
```

to:

```csharp
            AppTheme.SquishyFoxPlush => "ThemeBackgrounds/SquishyFoxPlushThemeBackground.xaml",
            AppTheme.Puca => "ThemeBackgrounds/PucaThemeBackground.xaml",
            _ => "ThemeBackgrounds/VoidCrystalThemeBackground.xaml"
```

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/MainWindow.xaml.cs
git commit -m "Register Puca background in MainWindow"
```

---

### Task 5: Add Púca to the theme picker + IsPucaThemeSelected

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:748` (ThemeOptions)
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:2823` (IsPucaThemeSelected)
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:3818` (RaiseThemeStateChanged)

- [ ] **Step 1: Add `ThemeOption` entry**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, the `ThemeOptions` array ends at line 748-749. Change:

```csharp
            new ThemeOption(AppTheme.SquishyFoxPlush, "Squishy Fox Plush")
        ];
```

to:

```csharp
            new ThemeOption(AppTheme.SquishyFoxPlush, "Squishy Fox Plush"),
            new ThemeOption(AppTheme.Puca, "Púca")
        ];
```

- [ ] **Step 2: Add `IsPucaThemeSelected` property**

In the same file, line 2823 has the last per-theme property. Change:

```csharp
    public bool IsSquishyFoxPlushThemeSelected => SelectedTheme == AppTheme.SquishyFoxPlush;

    public bool HasCustomThemeBackgroundImage
```

to:

```csharp
    public bool IsSquishyFoxPlushThemeSelected => SelectedTheme == AppTheme.SquishyFoxPlush;

    public bool IsPucaThemeSelected => SelectedTheme == AppTheme.Puca;

    public bool HasCustomThemeBackgroundImage
```

- [ ] **Step 3: Add `RaisePropertyChanged` call**

In the same file, inside `RaiseThemeStateChanged()` (line 3800), the last per-theme notification is at line 3818. Change:

```csharp
        RaisePropertyChanged(nameof(IsSquishyFoxPlushThemeSelected));
        RaisePropertyChanged(nameof(HasCustomThemeBackgroundImage));
```

to:

```csharp
        RaisePropertyChanged(nameof(IsSquishyFoxPlushThemeSelected));
        RaisePropertyChanged(nameof(IsPucaThemeSelected));
        RaisePropertyChanged(nameof(HasCustomThemeBackgroundImage));
```

- [ ] **Step 4: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED. "Púca" now appears in the theme ComboBox.

- [ ] **Step 5: Commit**

```
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "Add Puca to theme picker and selection state"
```

---

### Task 6: Add Púca branch to ThemedDialogWindow

**Files:**
- Modify: `VrcTwitchOscBridge\ThemedDialogWindow.xaml.cs` (after the `SquishyFoxPlush` branch or before the first `if` in `ApplyTheme`)

- [ ] **Step 1: Add the per-theme branch**

In `VrcTwitchOscBridge\ThemedDialogWindow.xaml.cs`, the `ApplyTheme` method starts at line 172. It has a series of `if (theme == AppTheme.X) { ... return; }` blocks. Add a Púca branch. The simplest insertion point is right after the opening brace of `ApplyTheme` (line 173), before the first `if (theme == AppTheme.Baked)`.

Insert this block immediately after `private void ApplyTheme(AppTheme theme)\n    {` and before `if (theme == AppTheme.Baked)`:

```csharp
        if (theme == AppTheme.Puca)
        {
            Resources["BodyFontFamily"] = new FontFamily("Verdana");
            Resources["HeadingFontFamily"] = new FontFamily("Cambria");
            SetBrushColor("WindowBackgroundBrush", "#0C0716");
            SetBrushColor("PanelBrush", "#E6140C24");
            SetBrushColor("BorderBrush", "#3A2868");
            SetBrushColor("AccentBrush", "#22D3EE");
            SetBrushColor("TextBrush", "#E8DEF8");
            SetBrushColor("MutedBrush", "#A896C8");
            SetBrushColor("InputBrush", "#E6080410");
            SetBrushColor("InputBorderBrush", "#4A2D8A");
            SetBrushColor("StatusChipBrush", "#2CA78BFA");
            SetBrushColor("SecondaryButtonBrush", "#2AF5B8E0");
            SetBrushColor("SecondaryButtonBorderBrush", "#F5B8E0");
            SetBrushColor("TitleBarBrush", "#1A0E2E");
            SetBrushColor("TitleBarTextBrush", "#7DEFFF");
            SetBrushColor("TitleBarSubTextBrush", "#F5B8E0");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3A2868");
            SetBrushColor("TitleBarButtonPressedBrush", "#4A3878");
            SetBrushColor("TitleBarCloseHoverBrush", "#C0395F");
            SetBrushColor("TitleBarClosePressedBrush", "#8C2648");
            return;
        }

```

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/ThemedDialogWindow.xaml.cs
git commit -m "Add Puca theme branch to ThemedDialogWindow"
```

---

### Task 7: Add Púca branch to AvatarRouletPickerWindow

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarRouletPickerWindow.xaml.cs` (in `ApplyTheme`, ~line 166)

- [ ] **Step 1: Add the per-theme branch**

In `VrcTwitchOscBridge\AvatarRouletPickerWindow.xaml.cs`, `ApplyTheme` starts at line 166. Insert the Púca branch immediately after `private void ApplyTheme(AppTheme theme)\n    {` and before `if (theme == AppTheme.Baked)`:

```csharp
        if (theme == AppTheme.Puca)
        {
            Resources["BodyFontFamily"] = new FontFamily("Verdana");
            Resources["HeadingFontFamily"] = new FontFamily("Cambria");
            SetBrushColor("WindowBackgroundBrush", "#0C0716");
            SetBrushColor("PanelBrush", "#E6140C24");
            SetBrushColor("BorderBrush", "#3A2868");
            SetBrushColor("AccentBrush", "#22D3EE");
            SetBrushColor("TextBrush", "#E8DEF8");
            SetBrushColor("MutedBrush", "#A896C8");
            SetBrushColor("InputBrush", "#E6080410");
            SetBrushColor("InputBorderBrush", "#4A2D8A");
            SetBrushColor("StatusChipBrush", "#2CA78BFA");
            SetBrushColor("SecondaryButtonBrush", "#2AF5B8E0");
            SetBrushColor("SecondaryButtonBorderBrush", "#F5B8E0");
            SetBrushColor("TitleBarBrush", "#1A0E2E");
            SetBrushColor("TitleBarTextBrush", "#7DEFFF");
            SetBrushColor("TitleBarSubTextBrush", "#F5B8E0");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3A2868");
            SetBrushColor("TitleBarButtonPressedBrush", "#4A3878");
            SetBrushColor("TitleBarCloseHoverBrush", "#C0395F");
            SetBrushColor("TitleBarClosePressedBrush", "#8C2648");
            return;
        }

```

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/AvatarRouletPickerWindow.xaml.cs
git commit -m "Add Puca theme branch to AvatarRouletPickerWindow"
```

---

### Task 8: Add Púca branch to RuleLockoutPickerWindow

**Files:**
- Modify: `VrcTwitchOscBridge\RuleLockoutPickerWindow.xaml.cs` (in `ApplyTheme`, ~line 172)

- [ ] **Step 1: Add the per-theme branch**

In `VrcTwitchOscBridge\RuleLockoutPickerWindow.xaml.cs`, `ApplyTheme` starts at line 172. This window also sets `PanelSecondaryBrush` (see the Baked branch at line 180). Insert the Púca branch immediately after `private void ApplyTheme(AppTheme theme)\n    {` and before `if (theme == AppTheme.Baked)`:

```csharp
        if (theme == AppTheme.Puca)
        {
            Resources["BodyFontFamily"] = new FontFamily("Verdana");
            Resources["HeadingFontFamily"] = new FontFamily("Cambria");
            SetBrushColor("WindowBackgroundBrush", "#0C0716");
            SetBrushColor("PanelBrush", "#E6140C24");
            SetBrushColor("PanelSecondaryBrush", "#D910081A");
            SetBrushColor("BorderBrush", "#3A2868");
            SetBrushColor("AccentBrush", "#22D3EE");
            SetBrushColor("TextBrush", "#E8DEF8");
            SetBrushColor("MutedBrush", "#A896C8");
            SetBrushColor("InputBrush", "#E6080410");
            SetBrushColor("InputBorderBrush", "#4A2D8A");
            SetBrushColor("StatusChipBrush", "#2CA78BFA");
            SetBrushColor("SecondaryButtonBrush", "#2AF5B8E0");
            SetBrushColor("SecondaryButtonBorderBrush", "#F5B8E0");
            SetBrushColor("TitleBarBrush", "#1A0E2E");
            SetBrushColor("TitleBarTextBrush", "#7DEFFF");
            SetBrushColor("TitleBarSubTextBrush", "#F5B8E0");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3A2868");
            SetBrushColor("TitleBarButtonPressedBrush", "#4A3878");
            SetBrushColor("TitleBarCloseHoverBrush", "#C0395F");
            SetBrushColor("TitleBarClosePressedBrush", "#8C2648");
            return;
        }

```

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/RuleLockoutPickerWindow.xaml.cs
git commit -m "Add Puca theme branch to RuleLockoutPickerWindow"
```

---

### Task 9: Add Púca branch to VrChatTwoFactorWindow

**Files:**
- Modify: `VrcTwitchOscBridge\VrChatTwoFactorWindow.xaml.cs` (in `ApplyTheme`, ~line 98)

- [ ] **Step 1: Add the per-theme branch**

In `VrcTwitchOscBridge\VrChatTwoFactorWindow.xaml.cs`, `ApplyTheme` starts at line 98. This window also sets combo brushes (see Baked branch lines 112-114). Insert the Púca branch immediately after `private void ApplyTheme(AppTheme theme)\n    {` and before `if (theme == AppTheme.Baked)`:

```csharp
        if (theme == AppTheme.Puca)
        {
            Resources["BodyFontFamily"] = new FontFamily("Verdana");
            Resources["HeadingFontFamily"] = new FontFamily("Cambria");
            SetBrushColor("WindowBackgroundBrush", "#0C0716");
            SetBrushColor("PanelBrush", "#E6140C24");
            SetBrushColor("BorderBrush", "#3A2868");
            SetBrushColor("AccentBrush", "#22D3EE");
            SetBrushColor("TextBrush", "#E8DEF8");
            SetBrushColor("MutedBrush", "#A896C8");
            SetBrushColor("InputBrush", "#E6080410");
            SetBrushColor("InputBorderBrush", "#4A2D8A");
            SetBrushColor("ComboSurfaceBrush", "#1A1030");
            SetBrushColor("ComboTextBrush", "#E8DEF8");
            SetBrushColor("ComboHighlightBrush", "#22D3EE");
            SetBrushColor("StatusChipBrush", "#2CA78BFA");
            SetBrushColor("SecondaryButtonBrush", "#2AF5B8E0");
            SetBrushColor("SecondaryButtonBorderBrush", "#F5B8E0");
            return;
        }

```

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/VrChatTwoFactorWindow.xaml.cs
git commit -m "Add Puca theme branch to VrChatTwoFactorWindow"
```

---

### Task 10: Add Púca branch to VrChatLoginWindow

**Files:**
- Modify: `VrcTwitchOscBridge\VrChatLoginWindow.xaml.cs` (in `ApplyTheme`, ~line 72)

- [ ] **Step 1: Add the per-theme branch**

In `VrcTwitchOscBridge\VrChatLoginWindow.xaml.cs`, `ApplyTheme` starts at line 72. This window also sets combo brushes (see Baked branch lines 86-88). Insert the Púca branch immediately after `private void ApplyTheme(AppTheme theme)\n    {` and before `if (theme == AppTheme.Baked)`:

```csharp
        if (theme == AppTheme.Puca)
        {
            Resources["BodyFontFamily"] = new FontFamily("Verdana");
            Resources["HeadingFontFamily"] = new FontFamily("Cambria");
            SetBrushColor("WindowBackgroundBrush", "#0C0716");
            SetBrushColor("PanelBrush", "#E6140C24");
            SetBrushColor("BorderBrush", "#3A2868");
            SetBrushColor("AccentBrush", "#22D3EE");
            SetBrushColor("TextBrush", "#E8DEF8");
            SetBrushColor("MutedBrush", "#A896C8");
            SetBrushColor("InputBrush", "#E6080410");
            SetBrushColor("InputBorderBrush", "#4A2D8A");
            SetBrushColor("ComboSurfaceBrush", "#1A1030");
            SetBrushColor("ComboTextBrush", "#E8DEF8");
            SetBrushColor("ComboHighlightBrush", "#22D3EE");
            SetBrushColor("StatusChipBrush", "#2CA78BFA");
            SetBrushColor("SecondaryButtonBrush", "#2AF5B8E0");
            SetBrushColor("SecondaryButtonBorderBrush", "#F5B8E0");
            return;
        }

```

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/VrChatLoginWindow.xaml.cs
git commit -m "Add Puca theme branch to VrChatLoginWindow"
```

---

### Task 11: Add Púca branch to TwitchChatboxWindow

**Files:**
- Modify: `VrcTwitchOscBridge\TwitchChatboxWindow.xaml.cs` (in `ApplyTheme`, ~line 510)

This window uses the **newer pattern** for recent themes (TreetendersArm, CarrotPatch, Bratwurst, StinkyOnline, SquishyFoxPlush): call `ThemeManager.ApplyToResources(Resources, theme)` then set 5 chatbox-specific brushes.

- [ ] **Step 1: Add the per-theme branch**

In `VrcTwitchOscBridge\TwitchChatboxWindow.xaml.cs`, `ApplyTheme` starts at line 510. The `SquishyFoxPlush` branch ends at line 577 with `return;` and the older `Baked` branch begins at line 579. Insert the Púca branch between line 577 and line 579 (after the `SquishyFoxPlush` block's closing `}` and before `if (theme == AppTheme.Baked)`).

Insert:

```csharp
        if (theme == AppTheme.Puca)
        {
            ThemeManager.ApplyToResources(Resources, theme);
            SetBrushColor("MessageTextBrush", "#E8DEF8");
            SetBrushColor("MessageCardBrush", "#A8140C24");
            SetBrushColor("MessageBorderBrush", "#4A2D8A");
            SetBrushColor("TimestampBrush", "#A896C8");
            SetBrushColor("SecondaryButtonTextBrush", "#7DEFFF");
            return;
        }

```

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```
git add VrcTwitchOscBridge/TwitchChatboxWindow.xaml.cs
git commit -m "Add Puca theme branch to TwitchChatboxWindow"
```

---

### Task 12: Update AGENTS.md theme list

**Files:**
- Modify: `AGENTS.md` (the "Current Themes" bulleted list)

- [ ] **Step 1: Add `Púca` to the theme list**

In `AGENTS.md`, find the "Current Themes" section. The last bullet is `- \`Squishy Fox Plush\``. Add a new bullet after it:

Change:

```
- `Squishy Fox Plush`
```

to:

```
- `Squishy Fox Plush`
- `Púca`
```

- [ ] **Step 2: Commit**

```
git add AGENTS.md
git commit -m "Add Puca to AGENTS.md theme list"
```

---

### Task 13: Final build verification + localization audit

**Files:**
- None (verification only)

- [ ] **Step 1: Full build**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: BUILD SUCCEEDED with no errors or warnings related to Púca.

- [ ] **Step 2: Run the localization audit**

Per AGENTS.md, run the localization audit. No new localization keys were added (theme names are not localized), but the audit enforces coverage/placeholder integrity and must pass.

Find and run the localization audit project:
```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj"
```
Expected: audit passes with no missing keys or placeholder errors. (If the audit project has a different run mechanism, check `LocalizationAudit\` for a README or script, or run it via the build scripts' localization-audit step.)

- [ ] **Step 3: Manual smoke test (visual confirmation)**

Launch the debug build:
```
"E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Then verify in the running app:
1. Open Settings → Visual → Theme dropdown → "Púca" appears
2. Select "Púca" — main window background renders the fused aurora/circle/constellation art with the centered cyan crescent moon and pink spark
3. Panels, borders, buttons, scrollbar, title bar use the near-black / dual cyan+pink palette
4. Headings render in Cambria, body text in Verdana
5. Title bar shows cyan text with pink "Púca" sub-text
6. Open each of these secondary windows and confirm Púca renders correctly:
   - ThemedDialogWindow (any themed dialog)
   - AvatarRouletPickerWindow (Avatar Roulette picker)
   - RuleLockoutPickerWindow (Rule Lockout picker)
   - VrChatTwoFactorWindow (VRChat 2FA — trigger VRChat login flow)
   - VrChatLoginWindow (VRChat login)
   - TwitchChatboxWindow (Twitch Chatbox)
7. Switch away from Púca to another theme, then back to Púca — confirm live theme update works without restart

If any window does not render Púca correctly, double-check its `ApplyTheme` branch from Tasks 6-11.

- [ ] **Step 4: Report completion**

Report the completed build with a version reminder per AGENTS.md:
`Last stable: 3.1.8; active development build: 3.1.9`
