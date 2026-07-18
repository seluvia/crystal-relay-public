# UI Update One-Time Notice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a one-time "big and noticeable" themed dialog warning users about the major UI update and asking them to verify their reward configurations.

**Architecture:** Reuses the existing `ThemedDialogWindow` infrastructure with a new `ShowNotice` static method that creates a wider, larger-font variant with a colored accent strip. Follows the exact same one-time-dismissal pattern as `AvatarSwapMigrationNoticeShown` / `CashPaymentMigrationNoticeShown`.

**Tech Stack:** C#, WPF/XAML, .NET 10

## Global Constraints

- Follow existing one-time notice patterns (AppSettings flag → ViewModel property/command → MainWindow startup call)
- Use `ThemedDialogWindow` — do not create a new window type
- `ThemedDialogWindow.xaml` changes must keep backward compatibility with all existing callers (ShowOk, ShowYesNo, ShowThreeChoice)
- The accent strip only shows in notice mode; normal dialogs are unaffected
- Setting is `UiUpdateNoticeShown` (bool, default false) in `AppSettings.cs`
- No localization keys needed for this one-time English notice

---

### Task 1: One-Time Notice Tracking Flag

**Files:**
- Modify: `VrcTwitchOscBridge\Models\AppSettings.cs:523-524`
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs:418-419, 602-603, 2843-2847`

**Interfaces:**
- Consumes: existing `AppSettings` class, `SettingsStore` profile/serialization pattern
- Produces: `AppSettings.UiUpdateNoticeShown` property, `SettingsProfile.UiUpdateNoticeShown` JSON field

- [ ] **Step 1: Add flag to AppSettings.cs**

After line 524 (`CashPaymentMigrationNoticeShown`), add:

```csharp
public bool UiUpdateNoticeShown { get; set; }
```

- [ ] **Step 2: Add deserialization in SettingsStore.cs**

After line 419 (`settings.CashPaymentMigrationNoticeShown = ...`), add:

```csharp
settings.UiUpdateNoticeShown = profile.UiUpdateNoticeShown ?? settings.UiUpdateNoticeShown;
```

- [ ] **Step 3: Add serialization in SettingsStore.cs**

After line 603 (`CashPaymentMigrationNoticeShown = settings.CashPaymentMigrationNoticeShown`), add:

```csharp
UiUpdateNoticeShown = settings.UiUpdateNoticeShown,
```

- [ ] **Step 4: Add JSON property in SettingsProfile**

After line 2847 (`cashPaymentMigrationNoticeShown`), add:

```csharp
[JsonPropertyName("uiUpdateNoticeShown")]
public bool? UiUpdateNoticeShown { get; set; }
```

---

### Task 2: Enhanced ThemedDialogWindow — XAML

**Files:**
- Modify: `VrcTwitchOscBridge\ThemedDialogWindow.xaml`

**Interfaces:**
- Consumes: existing theme resource system
- Produces: `ThemedDialogWindow` with bindable `IsNotice`, `HeadingFontSize`, `BodyFontSize`, `FinePrintFontSize` properties; accent strip visible only in notice mode

- [ ] **Step 1: Add accent strip and bindable font sizes to XAML**

Replace the content grid (Row 1 of the outer Grid) with this version. The changes are:
- Add a 4px accent Border at the top of the content area (Row 0 of inner Grid), visible only when `IsNotice` is true
- Change `HeaderTextBlock.FontSize` from hardcoded `24` to `{Binding HeadingFontSize, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}`
- Change `MessageTextBlock.FontSize` from hardcoded to `{Binding BodyFontSize, RelativeSource=...}`
- Change `FinePrintTextBlock.FontSize` from hardcoded `11` to `{Binding FinePrintFontSize, RelativeSource=...}`
- FinePrintTextBlock visibility: `{Binding FinePrintVisibility, RelativeSource=...}` instead of collapsed

Replace the content panel area (lines 199-276) in ThemedDialogWindow.xaml:

```xml
<Grid Grid.Row="1"
      Margin="22">
    <Border Background="{DynamicResource PanelBrush}"
            BorderBrush="{DynamicResource BorderBrush}"
            BorderThickness="1"
            CornerRadius="22"
            Padding="24">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <!-- Accent strip (notice mode only) -->
            <Border x:Name="AccentStrip"
                    Grid.Row="0"
                    Height="4"
                    Margin="0,0,0,14"
                    CornerRadius="2"
                    Background="{DynamicResource AccentBrush}"
                    Visibility="{Binding AccentStripVisibility, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}" />

            <DockPanel Grid.Row="1">
                <Image Source="Assets/crystal-relay-icon.png"
                       Width="36"
                       Height="36"
                       Margin="0,0,12,0"
                       Stretch="Uniform" />
                <StackPanel>
                    <TextBlock x:Name="HeaderTextBlock"
                               Style="{StaticResource HeadingTextStyle}"
                               FontSize="{Binding HeadingFontSize, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}"
                               FontWeight="Bold"
                               Foreground="{DynamicResource TextBrush}" />
                    <TextBlock Margin="0,6,0,0"
                               Text="Crystal Relay"
                               Foreground="{DynamicResource MutedBrush}" />
                </StackPanel>
            </DockPanel>

            <Border Grid.Row="2"
                    Margin="0,18,0,0"
                    Padding="14,12"
                    Background="{DynamicResource StatusChipBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    BorderThickness="1"
                    CornerRadius="16">
                <TextBlock x:Name="MessageTextBlock"
                           Foreground="{DynamicResource TextBrush}"
                           FontSize="{Binding BodyFontSize, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}"
                           TextWrapping="Wrap" />
            </Border>

            <TextBlock x:Name="FinePrintTextBlock"
                       Grid.Row="3"
                       Margin="6,10,6,0"
                       Foreground="{DynamicResource MutedBrush}"
                       FontSize="{Binding FinePrintFontSize, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}"
                       TextWrapping="Wrap" />

            <WrapPanel Grid.Row="4"
                       Margin="0,16,0,0"
                       Orientation="Horizontal"
                       HorizontalAlignment="Right"
                       VerticalAlignment="Center">
                <Button x:Name="TertiaryButton"
                        Margin="8,6,0,0"
                        Style="{StaticResource SecondaryButtonStyle}"
                        Visibility="Collapsed"
                        Click="OnTertiaryClicked" />
                <Button x:Name="SecondaryButton"
                        Margin="8,6,0,0"
                        Style="{StaticResource SecondaryButtonStyle}"
                        Visibility="Collapsed"
                        IsCancel="True"
                        Click="OnSecondaryClicked" />
                <Button x:Name="PrimaryButton"
                        Margin="8,6,0,0"
                        Style="{StaticResource PrimaryButtonStyle}"
                        IsDefault="True"
                        Click="OnPrimaryClicked" />
            </WrapPanel>
        </Grid>
    </Border>
</Grid>
```

---

### Task 3: Enhanced ThemedDialogWindow — Code-Behind

**Files:**
- Modify: `VrcTwitchOscBridge\ThemedDialogWindow.xaml.cs`

**Interfaces:**
- Consumes: `AppTheme` enum, `ThemeManager`, `LocalizationService`
- Produces: `ThemedDialogWindow.ShowNotice(owner, theme, title, message, finePrint)` static method

- [ ] **Step 1: Add properties and new constructor overload**

Add after the existing fields (after `SelectedChoice` property around line 56):

```csharp
public bool IsNotice { get; }
public double HeadingFontSize { get; } = 24;
public double BodyFontSize { get; } = 13;
public double FinePrintFontSize { get; } = 11;
public Visibility AccentStripVisibility => IsNotice ? Visibility.Visible : Visibility.Collapsed;
```

Modify the existing constructor to accept optional notice parameters. Change the constructor signature to include optional `isNotice` and `finePrintFontSizeOverride`:

```csharp
private ThemedDialogWindow(
    AppTheme theme,
    string title,
    string message,
    string primaryButtonText,
    string? secondaryButtonText = null,
    string? tertiaryButtonText = null,
    string? finePrint = null,
    bool isNotice = false)
```

At the start of the constructor body (after `InitializeComponent()`), add:

```csharp
if (isNotice)
{
    IsNotice = true;
    HeadingFontSize = 28;
    BodyFontSize = 15;
    FinePrintFontSize = 13;
}
```

Then after setting `FinePrintTextBlock.Visibility`, also set `FinePrintTextBlock.Visibility` to `FinePrintVisibility` when fine print is provided (replace the existing fine print visibility logic around line 38-41 with):

```csharp
FinePrintTextBlock.Text = finePrint ?? string.Empty;
FinePrintTextBlock.Visibility = string.IsNullOrWhiteSpace(finePrint)
    ? Visibility.Collapsed
    : Visibility.Visible;
```

- [ ] **Step 2: Add the ShowNotice static method**

Add after `ShowThreeChoice` (around line 114):

```csharp
public static void ShowNotice(
    Window? owner,
    AppTheme theme,
    string title,
    string message,
    string? finePrint = null,
    string buttonText = "")
{
    buttonText = string.IsNullOrWhiteSpace(buttonText)
        ? LocalizationService.Translate("I Understand")
        : buttonText;
    var dialog = new ThemedDialogWindow(theme, title, message, buttonText, null, null, finePrint, isNotice: true)
    {
        Owner = owner,
        Width = 680,
        MinWidth = 680
    };

    dialog.ShowDialog();
}
```

---

### Task 4: MainWindowViewModel — Property, Command, Handler

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`

**Interfaces:**
- Consumes: `AppSettings.UiUpdateNoticeShown`, existing `RelayCommand` pattern
- Produces: `ShowUiUpdateNotice` property, `DismissUiUpdateNoticeCommand`, `DismissUiUpdateNotice()` handler

- [ ] **Step 1: Add property**

After `ShowCashPaymentMigrationNotice` (around line 1373), add:

```csharp
public bool ShowUiUpdateNotice =>
    !Settings.UiUpdateNoticeShown;
```

- [ ] **Step 2: Add command**

After `DismissCashPaymentMigrationNoticeCommand` (around line 2943), add:

```csharp
public RelayCommand DismissUiUpdateNoticeCommand { get; }
```

In the constructor (around line 909), add:

```csharp
DismissUiUpdateNoticeCommand = new RelayCommand(DismissUiUpdateNotice);
```

- [ ] **Step 3: Add handler**

After `DismissCashPaymentMigrationNotice()` (around line 3681), add:

```csharp
private void DismissUiUpdateNotice()
{
    if (Settings.UiUpdateNoticeShown)
    {
        return;
    }

    Settings.UiUpdateNoticeShown = true;
    _ = SaveSettingsAsync();
}
```

---

### Task 5: MainWindow — Startup Call

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `MainWindowViewModel.ShowUiUpdateNotice`, `ThemedDialogWindow.ShowNotice`, `MainWindowViewModel.SelectedTheme`, `MainWindowViewModel.DismissUiUpdateNoticeCommand`
- Produces: one-time dialog shown at startup

- [ ] **Step 1: Add the startup method**

After `ShowAvatarSwapMigrationNoticeIfNeeded()` (around line 168), add:

```csharp
private void ShowUiUpdateNoticeIfNeeded()
{
    if (!viewModel.ShowUiUpdateNotice)
    {
        return;
    }

    ThemedDialogWindow.ShowNotice(
        this,
        viewModel.SelectedTheme,
        LocalizationService.Translate("Major UI Update — Please Verify Your Rewards"),
        LocalizationService.Translate(
            "Crystal Relay's main layout has been reorganized. " +
            "Your reward configurations are still here, but some sections may have moved or look different.\n\n" +
            "Please review each of your reward systems to make sure everything transferred correctly:"),
        finePrint:
            " • Avatar Sets & Avatar Change\n" +
            " • Avatar Roulette\n" +
            " • Bits / Subs / Payment overrides\n" +
            " • Avatar Scaling\n" +
            " • Power Ups & Channel Point Rewards\n" +
            " • Universal Triggers\n" +
            " • Cash Payment rules\n\n" +
            LocalizationService.Translate("This notice will not appear again."));
    viewModel.DismissUiUpdateNoticeCommand.Execute(null);
}
```

- [ ] **Step 2: Add the startup call**

After line 149 (`ShowAvatarSwapMigrationNoticeIfNeeded();`), add:

```csharp
ShowUiUpdateNoticeIfNeeded();
```

---

### Build Verification

- [ ] **Build the project**

Run:
```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no errors or warnings.
