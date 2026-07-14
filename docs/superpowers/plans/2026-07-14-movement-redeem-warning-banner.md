# Movement Redeem Warning Banner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a yellow attention banner at the top of the Movement Redeems popup manager window warning about OSC movement redeem limitations.

**Architecture:** Static XAML banner using existing `AttentionBrush`/`AttentionBorderBrush` theme resources, with `{loc:Translate}` bindings for heading and body text. No ViewModel changes needed.

**Tech Stack:** WPF XAML, C#, JSON localization

## Global Constraints

- Banner must appear above the toolbar in `MovementRedeemsManagerWindow.xaml`.
- Use existing `AttentionBrush` (#46390F) and `AttentionBorderBrush` (#D9B84F) from the theme system.
- Text must use `{loc:Translate}` bindings for localization support.
- Localization keys go into `Resources\Localization\en-US.extra.json` and matching `.extra.json` files for all other locales.
- No ViewModel, no code-behind, no new resources needed.

---

### Task 1: Add banner XAML to MovementRedeemsManagerWindow

**Files:**
- Modify: `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml` (add banner between title bar and toolbar)

**Interfaces:**
- Consumes: `{DynamicResource AttentionBrush}`, `{DynamicResource AttentionBorderBrush}`, `{DynamicResource HeadingFontFamily}`, `{DynamicResource TextBrush}`
- Produces: Yellow attention banner visible at top of window

- [ ] **Step 1: Read the toolbar area to understand row structure**

Already done — the content grid at Grid.Row="1" has:
- `RowDefinition Height="Auto"` (Row 0: toolbar)
- `RowDefinition Height="*"` (Row 1: DataGrid)

- [ ] **Step 2: Insert a new row for the banner and add the banner XAML**

Change the Grid.RowDefinitions from 2 to 3 rows, insert the banner at Grid.Row="0", shift toolbar to Grid.Row="1", shift DataGrid to Grid.Row="2".

In `MovementRedeemsManagerWindow.xaml`, at line 674-678, change:
```xml
            <Grid Grid.Row="1" Margin="14">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>
```
to:
```xml
            <Grid Grid.Row="1" Margin="14">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <!-- Movement Redeem Notice -->
                <Border Grid.Row="0"
                        Margin="0,0,0,8"
                        Padding="14,10"
                        Background="{DynamicResource AttentionBrush}"
                        BorderBrush="{DynamicResource AttentionBorderBrush}"
                        BorderThickness="1"
                        CornerRadius="8">
                    <StackPanel>
                        <TextBlock Text="{loc:Translate 'Movement Redeem Notice'}"
                                   FontFamily="{DynamicResource HeadingFontFamily}"
                                   FontSize="15"
                                   FontWeight="Bold"
                                   Foreground="{DynamicResource TextBrush}"
                                   TextWrapping="Wrap" />
                        <TextBlock Margin="0,4,0,0"
                                   Text="{loc:Translate 'OSC movement redeems may not work as intended for all users. Some movement directions work in VR but not on desktop, and movement inputs or counter-inputs from redeems may not always produce the expected results.'}"
                                   Foreground="{DynamicResource TextBrush}"
                                   TextWrapping="Wrap" />
                    </StackPanel>
                </Border>
```

Then at line 680 (the toolbar border), change `Grid.Row="0"` to `Grid.Row="1"`.
Then at line 714 (the DataGrid), change `Grid.Row="1"` to `Grid.Row="2"`.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/MovementRedeemsManagerWindow.xaml
git commit -m "feat: add OSC movement redeem warning banner to Movement Redeems window"
```

### Task 2: Add localization keys

**Files:**
- Modify: `VrcTwitchOscBridge\Resources\Localization\en-US.extra.json`
- Modify: All other locale `.extra.json` files (de-DE, es-ES, fr-FR, it-IT, ja-JP, ko-KR, pl-PL, pt-BR, ru-RU, sv-SE, th-TH, zh-CN, zh-TW)

**Interfaces:**
- Consumes: Key names used in Task 1 XAML

- [ ] **Step 1: Add en-US source keys**

Add to end of `Resources\Localization\en-US.extra.json` (before closing brace):
```json
  "Movement Redeem Notice": "Movement Redeem Notice",
  "OSC movement redeems may not work as intended for all users. Some movement directions work in VR but not on desktop, and movement inputs or counter-inputs from redeems may not always produce the expected results.": "OSC movement redeems may not work as intended for all users. Some movement directions work in VR but not on desktop, and movement inputs or counter-inputs from redeems may not always produce the expected results."
```

- [ ] **Step 2: Add matching keys to all other locale `.extra.json` files**

The body text should be translated naturally for each locale. The heading `"Movement Redeem Notice"` should also be translated.

For each locale file under `Resources\Localization\`, add the two keys with translated values. Follow the existing tone and style of each locale's translations.

- [ ] **Step 3: Run the localization audit**

```bash
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj"
```

Verify no errors about missing keys or placeholder mismatches.

- [ ] **Step 4: Build and verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Ensure build succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/
git commit -m "feat: add localization keys for movement redeem warning banner"
```
