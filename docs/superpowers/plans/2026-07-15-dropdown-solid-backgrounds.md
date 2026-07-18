# Dropdown Solid Backgrounds Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make all ComboBox dropdown items and popup containers use solid (non-transparent) themed backgrounds across all reward-editing XAML files.

**Architecture:** Find-and-replace transparency in 11 XAML files — ComboBoxItem `Background="Transparent"` → themed solid brush; Popup inner Grid gets matching background.

**Tech Stack:** WPF XAML, themed resource brushes (ComboSurfaceBrush, InputBrush, PanelBrush)

## Global Constraints

- Keep existing Popup `AllowsTransparency="True"` and `PopupAnimation="Slide"` — only fill the transparent areas with solid backgrounds
- Use each file's existing popup border background brush for consistency
- No behavioral or layout changes — purely visual

---

### Task 1: Fix MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml:666-687` (Popup Grid), `VrcTwitchOscBridge\MainWindow.xaml:713-739` (ComboBoxItem)

- [ ] **Step 1: Add background to Popup Grid**

Change lines 672-673 from:
```xml
<Grid MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
      MaxHeight="{TemplateBinding MaxDropDownHeight}">
```
to:
```xml
<Grid MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
      MaxHeight="{TemplateBinding MaxDropDownHeight}"
      Background="{DynamicResource ComboSurfaceBrush}">
```

- [ ] **Step 2: Change ComboBoxItem Background from Transparent to ComboSurfaceBrush**

Change ComboBoxItem style setter at line 715 from `Background="Transparent"` to `Background="{DynamicResource ComboSurfaceBrush}"`.

---

### Task 2: Fix AvatarSetsManagerWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml:` lines around Popup (need exact reading) and line 338

- [ ] **Step 1: Read the exact Popup section**
- [ ] **Step 2: Add background to Popup Grid** (same pattern as Task 1)
- [ ] **Step 3: Change ComboBoxItem Border `Background="Transparent"` to `Background="{DynamicResource ComboSurfaceBrush}"`**

---

### Task 3: Fix AvatarScalingManagerWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml:` Popup Grid (lines 439-462), ComboBoxItem Border (line 496)

- [ ] **Step 1: Add background to Popup Grid** — `Background="{DynamicResource InputBrush}"`
- [ ] **Step 2: Change ComboBoxItem Border `Background="Transparent"` to `Background="{DynamicResource InputBrush}"`**

---

### Task 4: Fix BuiltInCommandsWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\BuiltInCommandsWindow.xaml:` Popup Grid and ComboBoxItem

- [ ] **Step 1: Read and fix Popup Grid + ComboBoxItem** following same pattern

---

### Task 5: Fix BugReportWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\BugReportWindow.xaml`

- [ ] **Step 1: Read and fix Popup Grid + ComboBoxItem**

---

### Task 6: Fix MovementRedeemsManagerWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml`

- [ ] **Step 1: Read and fix Popup Grid + ComboBoxItem** (uses InputBrush)

---

### Task 7: Fix TestModeWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\TestModeWindow.xaml`

- [ ] **Step 1: Read and fix Popup Grid + ComboBoxItem**

---

### Task 8: Fix UniversalTriggersManagerWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\UniversalTriggersManagerWindow.xaml`

- [ ] **Step 1: Read and fix Popup Grid + ComboBoxItem**

---

### Task 9: Fix TwitchChatboxWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\TwitchChatboxWindow.xaml`

- [ ] **Step 1: Read and fix Popup Grid + ComboBoxItem**

---

### Task 10: Fix VrChatLoginWindow.xaml and VrChatTwoFactorWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\VrChatLoginWindow.xaml`
- Modify: `VrcTwitchOscBridge\VrChatTwoFactorWindow.xaml`

- [ ] **Step 1: Check if these have custom ComboBoxItem templates with Transparent backgrounds — if so, fix**
- [ ] **Step 2: Check if these have Popup definitions that need Grid backgrounds**

---

### Task 11: Build and verify

- [ ] **Step 1: Build the project**
  ```
  dotnet build VrcTwitchOscBridge\VrcTwitchOscBridge.csproj --no-restore
  ```
  Expected: Build succeeds with no errors
