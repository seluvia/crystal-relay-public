# Power Up Avatar Swap Polish — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the broken inline Power Up trigger button from the Avatar Swap editor.

**Architecture:** Single XAML edit. The button at `AvatarSwapManagerWindow.xaml:425-427` creates a `TriggerRule` with `TriggerType.PowerUp` that gets stuffed into `ChannelPointRules`, but the runtime Power Up dispatcher only matches against `PowerUpRuleSnapshot` objects — so the button produces dead rules that never fire. Remove it; the roulette's "Power Up" button already navigates to the main Power Up tab where real linking works.

**Tech Stack:** WPF/XAML

## Global Constraints

- Do not change any C# code-behind or ViewModel logic.
- Do not touch the roulette "Power Up" button (line ~519).
- Do not touch the main Power Up tab or any other feature.

---

### Task 1: Remove the inline Power Up button from the swap editor

**Files:**
- Modify: `AvatarSwapManagerWindow.xaml:~425-427`

- [ ] **Step 1: Remove the Power-up button**

In `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarSwapManagerWindow.xaml`, remove this button element (around line 425-427):

```xml
<Button Content="⚡ Power-up" Command="{Binding DataContext.AddAdvancedTriggerCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="PowerUp" Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,4,4" />
```

The `AddAdvancedTrigger` method's `default` fallback still handles `"PowerUp"` if called programmatically, but no UI path reaches it — no need to touch the ViewModel.

- [ ] **Step 2: Verify the build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no errors or warnings related to the change.

- [ ] **Step 3: Commit**

```bash
git add "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarSwapManagerWindow.xaml"
git commit -m "fix: remove dead inline Power-up button from avatar swap editor"
```
