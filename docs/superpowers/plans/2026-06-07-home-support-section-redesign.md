# Home Page Support Section Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compact the Home page support section by replacing two large bordered cards with a single horizontal strip, and add explanatory popups for Ko-fi and Discord buttons.

**Architecture:** Reuse the existing `ThemedDialogWindow` for both popups. Modify the Home section XAML in `MainWindow.xaml` to use a compact `WrapPanel` with the "100% free" chip + Ko-fi button + Discord button. Update `MainWindowViewModel.cs` command handlers to show dialogs before opening browser links. Add new localization keys to all language files.

**Tech Stack:** C#, WPF, XAML, existing `ThemedDialogWindow`, existing `LocalizationService`.

---

### Task 1: Add localization keys to `en-US.json`

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.json`

Add the following keys before the closing `}` (after the last existing entry):

```json
  "Support Crystal Relay on Ko-fi": "Support Crystal Relay on Ko-fi",
  "Crystal Relay is completely free for everyone. If it helps your stream and you want to support development, you can leave a tip on Ko-fi. Every contribution helps keep the program free and growing.": "Crystal Relay is completely free for everyone. If it helps your stream and you want to support development, you can leave a tip on Ko-fi. Every contribution helps keep the program free and growing.",
  "Open Ko-fi": "Open Ko-fi",
  "Join the Crystal Relay Discord": "Join the Crystal Relay Discord",
  "Get live update pings, sneak peeks, dev-related information, and meet other Crystal Relay users.": "Get live update pings, sneak peeks, dev-related information, and meet other Crystal Relay users.",
  "Open Discord": "Open Discord"
```

- [ ] **Step 1: Add keys to `en-US.json`**

- [ ] **Step 2: Add keys to all other `.json` files**

For each language file (`de-DE.json`, `es-ES.json`, `fr-FR.json`, `it-IT.json`, `ja-JP.json`, `ko-KR.json`, `pl-PL.json`, `pt-BR.json`, `ru-RU.json`, `sv-SE.json`, `th-TH.json`, `zh-CN.json`, `zh-TW.json`), append the same keys with the English values as placeholders (to be translated later or via the localization audit). Use the exact same JSON snippet as above.

- [ ] **Step 3: Add keys to all `.extra.json` files**

For each `.extra.json` file in the same folder, append the same keys.

---

### Task 2: Update `OpenKoFiSupportPage` in `MainWindowViewModel.cs`

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs:17902-17905`

Replace the existing method:

```csharp
    private void OpenKoFiSupportPage()
    {
        OpenUri(KoFiSupportUri);
    }
```

With:

```csharp
    private void OpenKoFiSupportPage()
    {
        var shouldOpenKoFi = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Support Crystal Relay on Ko-fi"),
            T("Crystal Relay is completely free for everyone. If it helps your stream and you want to support development, you can leave a tip on Ko-fi. Every contribution helps keep the program free and growing."),
            T("Open Ko-fi"),
            T("Close"));

        if (shouldOpenKoFi)
        {
            OpenUri(KoFiSupportUri);
        }
    }
```

- [ ] **Step 1: Replace `OpenKoFiSupportPage` body**

---

### Task 3: Update `OpenDiscordInvite` in `MainWindowViewModel.cs`

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs:17912-17926`

Replace the existing method:

```csharp
    private void OpenDiscordInvite()
    {
        var shouldOpenDiscord = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Join the Crystal Relay Discord"),
            $"{T("Join the Crystal Relay Discord for update pings, beta announcements, and community news.")}{Environment.NewLine}{Environment.NewLine}{T("This invite is temporary-protected. If you join while offline or leave before receiving the verification role, Discord may automatically remove you from the server. If that happens, just rejoin while you are online and try the verification step again.")}",
            T("Open Discord"),
            T("Close"));

        if (shouldOpenDiscord)
        {
            OpenUri(DiscordInviteUri);
        }
    }
```

With:

```csharp
    private void OpenDiscordInvite()
    {
        var shouldOpenDiscord = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Join the Crystal Relay Discord"),
            T("Get live update pings, sneak peeks, dev-related information, and meet other Crystal Relay users."),
            T("Open Discord"),
            T("Close"));

        if (shouldOpenDiscord)
        {
            OpenUri(DiscordInviteUri);
        }
    }
```

- [ ] **Step 1: Replace `OpenDiscordInvite` body**

---

### Task 4: Compact the Home page support section in `MainWindow.xaml`

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml:2076-2158`

Replace the entire `StackPanel` that starts at line 2076 (`<StackPanel Margin="0,14,0,0">`) and ends at line 2159 (`</StackPanel>`) with a compact `WrapPanel`:

```xml
                            <WrapPanel Margin="0,14,0,0" VerticalAlignment="Center">
                                <Border Background="{DynamicResource StatusChipBrush}"
                                        BorderBrush="{DynamicResource InputBorderBrush}"
                                        BorderThickness="1"
                                        CornerRadius="12"
                                        Padding="9,4"
                                        Margin="0,0,10,0"
                                        VerticalAlignment="Center">
                                    <TextBlock Text="{loc:Translate '100% free, always'}"
                                               FontSize="12"
                                               FontWeight="SemiBold"
                                               Foreground="{DynamicResource MutedBrush}" />
                                </Border>
                                <Button MinWidth="158"
                                        Padding="18,10"
                                        Margin="0,0,8,0"
                                        Style="{StaticResource PrimaryButtonStyle}"
                                        Content="{loc:Translate 'Support on Ko-fi'}"
                                        Command="{Binding OpenKoFiSupportCommand}" />
                                <Button MinWidth="136"
                                        Padding="16,9"
                                        Style="{StaticResource SecondaryButtonStyle}"
                                        Content="{loc:Translate 'Join Discord'}"
                                        Command="{Binding OpenDiscordInviteCommand}" />
                            </WrapPanel>
```

- [ ] **Step 1: Replace the support section StackPanel with WrapPanel**

---

### Task 5: Build and verify

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no errors.

- [ ] **Step 1: Build the project**

---

### Task 6: Run localization audit

Run the localization audit script (or build step) to verify all language files have the new keys and placeholder integrity is good.

- [ ] **Step 1: Run localization audit**

---

## Spec Coverage Checklist

| Requirement | Task |
|---|---|
| Compact horizontal strip replacing two large cards | Task 4 |
| Keep "Support Crystal Relay" heading + ? help icon | No change needed — heading stays above the WrapPanel |
| Keep "100% free, always" chip visible | Task 4 |
| Ko-fi button opens popup with support explanation | Task 2 |
| Discord button opens popup with community explanation | Task 3 |
| Popups have primary action + close button | Task 2, Task 3 (using `ShowYesNo`) |
| Preserve existing button styles | Task 4 |
| Localization for all languages | Task 1 |

## Placeholder Scan

No placeholders found. All steps include exact code.

## Type Consistency

- `ThemedDialogWindow.ShowYesNo` signature matches existing usage.
- `T()` helper is `private static string T(string)` — consistent.
- Commands remain `RelayCommand` bound to existing properties.
