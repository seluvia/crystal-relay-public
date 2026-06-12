# Fix Scale Preview Text and Remove Current Textbox — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the scale preview text to use live OSC avatar height and remove the unnecessary "Current (meters)" textbox from all scale action modes.

**Architecture:** Add a `CurrentAvatarHeightMeters` property to the ViewModel that reads from the OSC-observed avatar height. Update the converter to use correct binding indices and the new current height. Remove "Current (meters)" textbox from Relative and Multiplier modes in both main and Cash Payment sections.

**Tech Stack:** C#, WPF/XAML, .NET 10

---

## File Structure

| File | Responsibility |
|------|---------------|
| `ViewModels/MainWindowViewModel.cs` | Add `CurrentAvatarHeightMeters` property, raise on status change |
| `MainWindow.xaml` | Remove "Current (meters)" textbox from 4 locations, update converter bindings |
| `Converters.cs` | Fix converter indices and preview text wording |

---

### Task 1: Add CurrentAvatarHeightMeters Property to ViewModel

**Files:**
- Modify: `ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add CurrentAvatarHeightMeters property**

Find the `AvatarScaleRuntimeStatusText` property (around line 1991). Add a new property after it:

```csharp
    public double CurrentAvatarHeightMeters
    {
        get
        {
            var status = bridgeCoordinator.GetAvatarScaleRuntimeStatus();
            return status.CurrentHeightMeters ?? 1.6;
        }
    }
```

- [ ] **Step 2: Raise property on status change**

Find `HandleAvatarScaleStatusChanged()` (around line 9306). After the existing `RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText))` line, add:

```csharp
        RaisePropertyChanged(nameof(CurrentAvatarHeightMeters));
```

- [ ] **Step 3: Build verification**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 2: Update Converter Bindings in XAML

**Files:**
- Modify: `MainWindow.xaml`

- [ ] **Step 1: Update main Avatar Scaling preview block bindings**

Find the MultiBinding in the live preview block (around line 7176). Replace the `MultCurrentPreview` binding:

```xml
<Binding Path="MultCurrentPreview" />
```

With:

```xml
<Binding Path="DataContext.CurrentAvatarHeightMeters" RelativeSource="{RelativeSource AncestorType=Window}" />
```

- [ ] **Step 2: Build verification**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 3: Fix Converter Logic and Preview Text

**Files:**
- Modify: `Converters.cs`

- [ ] **Step 1: Fix RelativeHeight case**

Replace the RelativeHeight case in the converter (around line 107-110):

```csharp
            "RelativeHeight" =>
                values.Length >= 6 && TryGetDouble(values[4], out var ch) && TryGetDouble(values[5], out var cu)
                    ? string.Format(culture, "Adds {0:+0.##;-0.##;0}m to your height, going from {1:0.##}m to {2:0.##}m.", ch, cu, cu + ch)
                    : "\u2014",
```

- [ ] **Step 2: Fix Multiplier case**

Replace the Multiplier case in the converter (around line 111-116):

```csharp
            "Multiplier" =>
                values.Length >= 7 && TryGetDouble(values[6], out var mul) && TryGetDouble(values[5], out var mcu) && values[7] is bool divide
                    ? string.Format(culture, divide
                        ? "Going from {0:0.##}m to {1:0.##}m using \u00F7{2:0.##}."
                        : "Going from {0:0.##}m to {1:0.##}m using \u00D7{2:0.##}.", mcu, divide && mul != 0 ? mcu / mul : mcu * mul, mul)
                    : "\u2014",
```

- [ ] **Step 3: Build verification**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 4: Remove "Current (meters)" Textbox from Main Section

**Files:**
- Modify: `MainWindow.xaml`

- [ ] **Step 1: Remove from Relative mode (main section)**

Find the "Current (meters)" StackPanel in the Relative mode variant (around lines 7086-7091). It looks like:

```xml
<StackPanel Grid.Column="1" Margin="7,0,0,0">
    <TextBlock Text="{loc:Translate 'Current (meters)'}"
               Foreground="{DynamicResource TextBrush}"
               FontWeight="SemiBold" />
    <TextBox Text="{Binding MultCurrentPreview, UpdateSourceTrigger=LostFocus}" />
</StackPanel>
```

Remove this entire StackPanel. Also change the Grid from 2 columns to 1 column (remove the Grid entirely and just have the "Change (meters)" StackPanel directly).

- [ ] **Step 2: Remove from Multiplier mode (main section)**

Find the "Current (meters)" StackPanel in the Multiplier mode variant (around lines 7131-7135). Remove this entire StackPanel. Also update the Grid from 3 columns to 2 columns (remove the third ColumnDefinition and the StackPanel in Grid.Column="2").

- [ ] **Step 3: Build verification**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 5: Remove "Current (meters)" Textbox from Cash Payment Section

**Files:**
- Modify: `MainWindow.xaml`

- [ ] **Step 1: Remove from Relative mode (Cash Payment section)**

Find the "Current (meters)" StackPanel in the Cash Payment Relative mode variant (around lines 8259-8263). Remove this entire StackPanel. Also change the Grid from 2 columns to 1 column.

- [ ] **Step 2: Remove from Multiplier mode (Cash Payment section)**

Find the "Current (meters)" StackPanel in the Cash Payment Multiplier mode variant (around lines 8299-8303). Remove this entire StackPanel. Also update the Grid from 3 columns to 2 columns.

- [ ] **Step 3: Build verification**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 6: Final Build Verification

- [ ] **Step 1: Clean build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds with 0 warnings and 0 errors.
