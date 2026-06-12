# Power Up Editor: Add Active Time and Bot Message

## Problem

The Power Up rule editor in `MainWindow.xaml` is missing two fields that other rule types (Avatar Set, Bits+Subs, Cash Payments) already expose:

1. **Active Time (seconds)** — `ActionRule.DurationSeconds` controls how long Crystal Relay holds the action active before resetting. Defaults to 10 but is never editable in the Power Up UI.
2. **Bot Message** — `ActionRule.BotMessageTemplate` is the chatbox message template. Not exposed in the Power Up editor.

## Scope

**Power Up ONLY.** No changes to Avatar Set, Bits+Subs, Cash Payment, Movement, Universal Trigger, Avatar Scaling, or any other section.

## Design

### Approach

New dedicated nested panel ("Power Up Action Settings") added after the existing "Power Up Rules" panel, before the "Test Power Up" button. Follows the existing panel grouping convention used throughout the Power Up editor.

### XAML Changes

File: `VrcTwitchOscBridge/MainWindow.xaml`

Insert a new `Border` panel inside the `PowerUpRule` DataTemplate, between the "Power Up Rules" `Border` (closes at ~line 5482) and the "Test Power Up" `WrapPanel` (~line 5484).

Panel structure:
- `Border` with `NestedPanelBrush` background, `HighlightBorderBrush` border, `CornerRadius="16"`, `Padding="14"`
- Visibility: Collapsed when `ActionKind == AvatarScaling` (via DataTrigger on `UsesAvatarScaling`), Visible otherwise
- Header: `TextBlock` "Power Up Action Settings" (FontSize 17, SemiBold)
- `UniformGrid` with 2 columns:
  - **Left column (Active Time):**
    - `DockPanel` with label "Active Time (seconds)" + help button (Tag bound to `ActionRule.DurationHelpText`)
    - `TextBox` bound to `ActionRule.DurationSeconds` with `UpdateSourceTrigger=PropertyChanged`
  - **Right column (Bot Message):**
    - `DockPanel` with label "Bot Message" + help button (Tag with localized help text about `{user}`, `{rule}`, `{duration}`, `{cooldown}` placeholders — matching the existing `BotMessageTemplate` convention)
    - `TextBox` bound to `ActionRule.BotMessageTemplate` with `AcceptsReturn="True"`, `Height="70"`, `TextWrapping="Wrap"`

### Visibility Logic

- `ActionKind == TriggerAction` → Panel visible
- `ActionKind == AvatarScaling` → Panel collapsed (Avatar Scaling section already exposes its own `ActiveTimeSeconds`)

### Localization

New keys in all 14 `.extra.json` files:

1. `"Power Up Action Settings"` — panel header
2. `"Crystal Relay sends this chatbox message when the Power Up fires. Use {user} for the viewer name, {rule} for the rule name, {duration} for active time, and {cooldown} for cooldown."` — Bot Message help text (matches the existing `BotMessageTemplate` placeholder convention used in `TriggerRule`)

Existing keys reused (already translated in all `.json` files):
- `"Active Time (seconds)"`
- `"Bot Message"`

### No Model/ViewModel Changes

`PowerUpRule.ActionRule` (a `TriggerRule` instance) already has:
- `DurationSeconds` (int, default 10, PropertyChanged notification)
- `BotMessageTemplate` (string, PropertyChanged notification)
- `DurationHelpText` (computed string, already used by other editors)

No new properties, commands, or ViewModel wiring needed.

### Files Changed

| File | Change |
|------|--------|
| `MainWindow.xaml` | New "Power Up Action Settings" panel in PowerUpRule DataTemplate |
| `en-US.extra.json` | Add 2 new localization keys |
| `de-DE.extra.json` | Add 2 placeholder keys |
| `es-ES.extra.json` | Add 2 placeholder keys |
| `fr-FR.extra.json` | Add 2 placeholder keys |
| `it-IT.extra.json` | Add 2 placeholder keys |
| `ja-JP.extra.json` | Add 2 translated keys |
| `ko-KR.extra.json` | Add 2 translated keys |
| `pl-PL.extra.json` | Add 2 placeholder keys |
| `pt-BR.extra.json` | Add 2 placeholder keys |
| `ru-RU.extra.json` | Add 2 placeholder keys |
| `sv-SE.extra.json` | Add 2 placeholder keys |
| `th-TH.extra.json` | Add 2 placeholder keys |
| `zh-CN.extra.json` | Add 2 translated keys |
| `zh-TW.extra.json` | Add 2 translated keys |

### Verification

1. Build: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
2. Visual: Power Up editor shows Active Time and Bot Message when Action Kind is "Trigger Action"
3. Visual: Fields hide when Action Kind is "Avatar Scaling"
4. Functional: Changing Active Time updates `ActionRule.DurationSeconds`
5. Functional: Changing Bot Message updates `ActionRule.BotMessageTemplate`
6. Localization audit: Run audit to verify no empty values
