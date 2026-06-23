# Float Transition In/Out Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `FloatTransitionSeconds` with separate `FloatTransitionInSeconds` and `FloatTransitionOutSeconds` and apply the eased transition to every Float avatar parameter redeem path (timed, instant, all action modes).

**Architecture:** Model adds two fields (replacing the one), the existing `SendFloatAvatarParameterValueAsync` ramp primitive is reused for both directions, a new unified helper handles the instant + action-mode path, and the editor exposes two inputs instead of one. Old saves migrate: `FloatTransitionSeconds` copies into both new fields.

**Tech Stack:** C# / .NET 10 / WPF / xUnit. Localization via `loc:Translate` markup and `.json` / `.extra.json` files. Settings persistence via `SettingsStore.PersistedTriggerRule`.

**Spec:** `docs/superpowers/specs/2026-06-23-float-transition-in-out-design.md`

---

## File map

- **Modify:** `VrcTwitchOscBridge/Models/TriggerRule.cs` — remove old field, add In/Out fields, update derived properties, update setters
- **Modify:** `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` — update `TriggerRuleSnapshot` record and mapping
- **Modify:** `VrcTwitchOscBridge/Services/SettingsStore.cs` — update `PersistedTriggerRule`, `ToPersistedRule`, `ToRule` (with migration)
- **Modify:** `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` — update timed Float path, add new instant/action-mode helper, update Glitchy tick loop
- **Modify:** `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml` — replace single TextBox with two, move out of timed-only parent, add help text
- **Modify:** `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` — update property change broadcast list
- **Modify:** `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs` — use new snapshot fields
- **Modify:** `VrcTwitchOscBridge.Tests/TriggerRuleFloatModePersistenceTests.cs` — add migration test, update round-trip test
- **Modify:** `VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs` — add tests for new In/Out derived properties
- **Modify:** `CHANGELOG.txt` — add bullet under `v3.1.9 beta 4`
- **Modify:** `RELEASE-CHANGE-RECORD.txt` — add internal note
- **Modify:** Localization `en-US` JSON source — add new keys
- **Modify:** Localization `.extra.json` files — add new key translations

---

## Task 1: Update TriggerRule model — remove old field, add In/Out fields

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs:96, 785-797`

- [ ] **Step 1: Remove old field and property**

Open `VrcTwitchOscBridge/Models/TriggerRule.cs`. Delete the `private double floatTransitionSeconds;` field declaration on line 96.

Delete the entire `FloatTransitionSeconds` property (lines 785-797):

```csharp
    public double FloatTransitionSeconds
    {
        get => floatTransitionSeconds;
        set
        {
            var normalizedValue = Math.Clamp(value, 0, 30);
            if (SetProperty(ref floatTransitionSeconds, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesFloatTransition));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }
```

- [ ] **Step 2: Add new In/Out fields next to the other Float fields**

In the field block (around line 96), replace the deleted `floatTransitionSeconds` declaration with:

```csharp
    private double floatTransitionInSeconds;
    private double floatTransitionOutSeconds;
```

- [ ] **Step 3: Add In/Out properties in the same place the old property was**

After the `FloatValueMode` property block, add:

```csharp
    public double FloatTransitionInSeconds
    {
        get => floatTransitionInSeconds;
        set
        {
            var normalizedValue = Math.Clamp(value, 0, 30);
            if (SetProperty(ref floatTransitionInSeconds, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesFloatInTransition));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatTransitionOutSeconds
    {
        get => floatTransitionOutSeconds;
        set
        {
            var normalizedValue = Math.Clamp(value, 0, 30);
            if (SetProperty(ref floatTransitionOutSeconds, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesFloatOutTransition));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }
```

- [ ] **Step 4: Build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: **build errors** (expected — many call sites still reference the old field/property). Move on; the next tasks fix them.

---

## Task 2: Replace `UsesFloatTransition` with In/Out derived properties

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs:1698, 1949-1988, 1124, 725, 793`

- [ ] **Step 1: Replace the `UsesFloatTransition` property**

In `VrcTwitchOscBridge/Models/TriggerRule.cs` at line 1698, delete:

```csharp
    public bool UsesFloatTransition => UsesFloatTimedValues && FloatTransitionSeconds > 0;
```

Replace with:

```csharp
    public bool UsesFloatInTransition => UsesFloatParameter && FloatTransitionInSeconds > 0;
    public bool UsesFloatOutTransition => UsesFloatParameter && FloatTransitionOutSeconds > 0;
```

- [ ] **Step 2: Update `RaiseActionVisibilityProperties` to raise the new properties**

In `RaiseActionVisibilityProperties` (line 1949 area), find the line:

```csharp
                RaisePropertyChanged(nameof(UsesFloatTransition));
```

Replace it with:

```csharp
                RaisePropertyChanged(nameof(UsesFloatInTransition));
                RaisePropertyChanged(nameof(UsesFloatOutTransition));
```

- [ ] **Step 3: Update the `DurationSeconds` setter**

In the `DurationSeconds` setter around line 1124, find the line:

```csharp
                    RaisePropertyChanged(nameof(UsesFloatTransition));
```

Replace it with the same two-line change as Step 2.

- [ ] **Step 4: Build (still expected to fail)**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: still has build errors. Continue.

---

## Task 3: Update `TriggerRuleSnapshot` record and mapping

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs:100, 956`

- [ ] **Step 1: Update the record declaration**

In `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` at line 100, find:

```csharp
    double FloatTransitionSeconds,
```

Replace with:

```csharp
    double FloatTransitionInSeconds,
    double FloatTransitionOutSeconds,
```

- [ ] **Step 2: Update the `ToTriggerRuleSnapshot` mapping**

Around line 956 in the same file, find:

```csharp
            Math.Clamp(rule.FloatTransitionSeconds, 0, 30),
```

Replace with:

```csharp
            Math.Clamp(rule.FloatTransitionInSeconds, 0, 30),
            Math.Clamp(rule.FloatTransitionOutSeconds, 0, 30),
```

- [ ] **Step 3: Update `TestTriggerRuleSnapshotBuilder` to use the new fields**

In `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs` at line 51, find:

```csharp
        FloatTransitionSeconds: 0,
```

Replace with:

```csharp
        FloatTransitionInSeconds: 0,
        FloatTransitionOutSeconds: 0,
```

- [ ] **Step 4: Build (still expected to fail)**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: still has build errors. Continue.

---

## Task 4: Update `SettingsStore.PersistedTriggerRule` field

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:3327`

- [ ] **Step 1: Add the new fields to the persisted class**

In `VrcTwitchOscBridge/Services/SettingsStore.cs` at line 3327, find:

```csharp
        public double FloatTransitionSeconds { get; set; }
```

Replace with:

```csharp
        public double FloatTransitionInSeconds { get; set; }
        public double FloatTransitionOutSeconds { get; set; }
        // FloatTransitionSeconds is intentionally not read or written. Old saves
        // carrying that key are migrated in the ToRule path (see ToRule body).
        [System.Text.Json.Serialization.JsonIgnore]
        public double FloatTransitionSeconds { get; set; }
```

- [ ] **Step 2: Build (still expected to fail)**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: still has build errors. Continue.

---

## Task 5: Update `SettingsStore.ToPersistedRule` to write the new fields

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:1049`

- [ ] **Step 1: Replace the old field write**

In `VrcTwitchOscBridge/Services/SettingsStore.cs` at line 1049, find:

```csharp
            FloatTransitionSeconds = rule.FloatTransitionSeconds,
```

Replace with:

```csharp
            FloatTransitionInSeconds = rule.FloatTransitionInSeconds,
            FloatTransitionOutSeconds = rule.FloatTransitionOutSeconds,
```

---

## Task 6: Update `SettingsStore.ToRule` — read new fields + migrate old field

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:1343`
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:1259` (the method body start)

- [ ] **Step 1: Update the read side**

In `VrcTwitchOscBridge/Services/SettingsStore.cs` at line 1343, find:

```csharp
            FloatTransitionSeconds = Math.Clamp(rule.FloatTransitionSeconds, 0, 30),
```

Replace with:

```csharp
            FloatTransitionInSeconds = Math.Clamp(rule.FloatTransitionInSeconds, 0, 30),
            FloatTransitionOutSeconds = Math.Clamp(rule.FloatTransitionOutSeconds, 0, 30),
```

- [ ] **Step 2: Add the migration step at the top of the `ToRule` body**

The `ToRule(PersistedTriggerRule rule)` method starts at line 1259. Insert a migration step immediately after the method signature line (before any other field assignment). The step:

```csharp
        // Migration: if the saved JSON has the old FloatTransitionSeconds key
        // and the new In/Out keys are 0, copy the old value into both and clear it.
        if (rule.FloatTransitionSeconds > 0
            && rule.FloatTransitionInSeconds <= 0
            && rule.FloatTransitionOutSeconds <= 0)
        {
            rule.FloatTransitionInSeconds = Math.Clamp(rule.FloatTransitionSeconds, 0, 30);
            rule.FloatTransitionOutSeconds = Math.Clamp(rule.FloatTransitionSeconds, 0, 30);
            rule.FloatTransitionSeconds = 0;
        }
```

The exact placement: after the `internal static TriggerRule ToRule(PersistedTriggerRule rule)` signature and the first `{`, before any other code in the body.

- [ ] **Step 3: Build (still expected to fail — runtime paths still reference old field)**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: still has build errors in `BridgeCoordinator.cs` and `MainWindowViewModel.cs`. Continue.

---

## Task 7: Write the migration test

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/TriggerRuleFloatModePersistenceTests.cs`

- [ ] **Step 1: Add a migration test**

Append a new `[Fact]` to the `TriggerRuleFloatModePersistenceTests` class. Place it before the `ToPersistedViaReflection` private helper.

```csharp
    [Fact]
    public void ToRule_OldFloatTransitionSeconds_MigratesToInAndOut()
    {
        // Simulates a JSON file written by a Crystal Relay that only had
        // the single FloatTransitionSeconds field.
        var persisted = new SettingsStore.PersistedTriggerRule
        {
            Id = Guid.NewGuid(),
            ParameterType = OscParameterType.Float,
            ParameterValue = "0.5",
            ResetValue = "0",
            FloatValueMode = FloatValueMode.Decimal,
            FloatTransitionSeconds = 2.0,
            // Intentionally do NOT set FloatTransitionInSeconds / FloatTransitionOutSeconds.
        };
        var rule = SettingsStore.ToRule(persisted);
        Assert.Equal(2.0, rule.FloatTransitionInSeconds);
        Assert.Equal(2.0, rule.FloatTransitionOutSeconds);
    }

    [Fact]
    public void ToRule_NewFieldsAlreadySet_AreNotOverwrittenByMigration()
    {
        // If a newer save file already has the new fields populated, the
        // migration must not overwrite them with the old value.
        var persisted = new SettingsStore.PersistedTriggerRule
        {
            Id = Guid.NewGuid(),
            ParameterType = OscParameterType.Float,
            FloatTransitionSeconds = 2.0,
            FloatTransitionInSeconds = 0.5,
            FloatTransitionOutSeconds = 1.5,
        };
        var rule = SettingsStore.ToRule(persisted);
        Assert.Equal(0.5, rule.FloatTransitionInSeconds);
        Assert.Equal(1.5, rule.FloatTransitionOutSeconds);
    }
```

- [ ] **Step 2: Run the test (expected to fail because the migration is not yet built)**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TriggerRuleFloatModePersistenceTests" --no-restore
```

Expected: build failure or test failure (the model field doesn't exist yet). The next tasks make it pass.

- [ ] **Step 3: Mark this step in-progress; do not commit yet**

The test will pass once Tasks 1, 4, and 6 are committed. Continue.

---

## Task 8: Update `BridgeCoordinator` runtime — timed Float path

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:7659, 7715, 7976`

- [ ] **Step 1: Update `IsTimedFloatAvatarParameterRule`**

At line 7663, find:

```csharp
        && (rule.FloatTransitionSeconds > 0 || rule.ActiveFloatBoostRewardEnabled);
```

Replace with:

```csharp
        && (rule.FloatTransitionInSeconds > 0
            || rule.FloatTransitionOutSeconds > 0
            || rule.ActiveFloatBoostRewardEnabled);
```

- [ ] **Step 2: Update `ExecuteTimedFloatAvatarParameterRuleActionAsync`**

At line 7715, find:

```csharp
        var transitionSeconds = Math.Clamp(rule.FloatTransitionSeconds, 0, 30);
        var activeSeconds = Math.Max(1, rule.DurationSeconds);
        var totalActiveSeconds = transitionSeconds + activeSeconds + transitionSeconds;
```

Replace with:

```csharp
        var inSeconds = Math.Clamp(rule.FloatTransitionInSeconds, 0, 30);
        var outSeconds = Math.Clamp(rule.FloatTransitionOutSeconds, 0, 30);
        var activeSeconds = Math.Max(1, rule.DurationSeconds);
        var totalActiveSeconds = inSeconds + activeSeconds + outSeconds;
```

Find every `transitionSeconds` reference in this method that was the local we just renamed. Update the call sites:

- The `SendFloatAvatarParameterValueAsync` call around line 7796: change the argument from `transitionSeconds` to `inSeconds`.
- The `ScheduleActiveFloatRedeemCompletion` call around line 7826: change the trailing `transitionSeconds` argument to `outSeconds`.

- [ ] **Step 3: Update the Glitchy release path in `ScheduleActiveFloatRedeemCompletion`**

At line 7976, find:

```csharp
        var transitionSeconds = Math.Clamp(rule.FloatTransitionSeconds, 0, 30);
```

Replace with:

```csharp
        var transitionSeconds = Math.Clamp(rule.FloatTransitionOutSeconds, 0, 30);
```

- [ ] **Step 4: Build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: still has build errors in `MainWindowViewModel.cs` and possibly the XAML.

---

## Task 9: Add the new unified Float transition helper

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (add a new method, place it after `SendFloatAvatarParameterValueAsync` at line 8149)

- [ ] **Step 1: Add the new helper**

Insert the following method directly after `SendFloatAvatarParameterValueAsync` (so it's right above `SendSingleFloatAvatarParameterValueAsync`):

```csharp
    private async Task ExecuteFloatAvatarParameterWithTransitionAsync(
        TriggerRuleSnapshot rule,
        CancellationToken cancellationToken)
    {
        var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);

        // Resolve the target value: Set mode uses ParameterValue, all other
        // action modes compute the next value from the current OSC reading.
        double targetValue;
        if (rule.FloatActionMode == FloatActionMode.Set)
        {
            if (!FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ParameterValue, out targetValue))
            {
                return;
            }
        }
        else
        {
            var currentForCompute = await TryGetCurrentAvatarFloatValueAsync(address, 0.0, cancellationToken);
            targetValue = FloatActionDispatch.ComputeNext(rule, currentForCompute).nextValue;
        }

        var inSeconds = Math.Clamp(rule.FloatTransitionInSeconds, 0, 30);
        var currentValue = await TryGetCurrentAvatarFloatValueAsync(address, targetValue, cancellationToken);

        if (inSeconds <= 0 || Math.Abs(currentValue - targetValue) < 0.000001d)
        {
            await SendSingleFloatAvatarParameterValueAsync(address, targetValue, cancellationToken);
            return;
        }

        await SendFloatAvatarParameterValueAsync(address, currentValue, targetValue, inSeconds, cancellationToken);
    }
```

- [ ] **Step 2: Build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: builds successfully. The helper exists but is not yet called from anywhere — that's the next task.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat(float): add unified Float transition helper for instant/action modes"
```

---

## Task 10: Wire the new helper into the non-timed dispatch path

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (find the existing non-timed Float action mode dispatch site)

- [ ] **Step 1: Locate the non-timed dispatch site**

Search `BridgeCoordinator.cs` for the function that dispatches a Float action mode redeem with `DurationSeconds <= 0` (the complement of `IsTimedFloatAvatarParameterRule`). Common location: near `ExecuteTimedFloatAvatarParameterRuleActionAsync`. Identify the function and its body.

- [ ] **Step 2: Replace the body to call the new helper**

In the non-timed function, replace the existing single-send body with:

```csharp
        await ExecuteFloatAvatarParameterWithTransitionAsync(rule, cancellationToken);
```

Keep the surrounding lock, lane-bookkeeping, cooldown, and log lines that are already in the function.

- [ ] **Step 3: Build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: builds successfully.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat(float): route non-timed Float action modes through transition helper"
```

---

## Task 11: Update Glitchy tick loop for transitions

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (Glitchy tick loop — find by searching for `FloatGlitchyIntervalMs`)

- [ ] **Step 1: Find the Glitchy tick handler**

Search `BridgeCoordinator.cs` for the function that fires a new random value at `FloatGlitchyIntervalMs` intervals. It is the per-tick body of the Glitchy mode.

- [ ] **Step 2: Replace the tick send with a transition call**

In the tick body, find the line that calls `SendSingleFloatAvatarParameterValueAsync` with the new glitchy value. Replace it with:

```csharp
        var inSeconds = Math.Clamp(rule.FloatTransitionInSeconds, 0, 30);
        var currentValue = await TryGetCurrentAvatarFloatValueAsync(address, newValue, cancellationToken);
        if (inSeconds <= 0 || Math.Abs(currentValue - newValue) < 0.000001d)
        {
            await SendSingleFloatAvatarParameterValueAsync(address, newValue, cancellationToken);
        }
        else
        {
            await SendFloatAvatarParameterValueAsync(address, currentValue, newValue, inSeconds, cancellationToken);
        }
        session.CurrentValue = newValue;
```

(Replace `newValue` with the variable name the tick body uses for the freshly computed glitchy value. Replace `address` with the variable name that holds the normalized address.)

- [ ] **Step 3: Build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: builds successfully.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat(float): smooth every Glitchy tick with Transition In"
```

---

## Task 12: Update `MainWindowViewModel` property broadcast

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs:200`

- [ ] **Step 1: Replace the old broadcast**

In `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` at line 200, find:

```csharp
        nameof(TriggerRule.FloatTransitionSeconds),
```

Replace with:

```csharp
        nameof(TriggerRule.FloatTransitionInSeconds),
        nameof(TriggerRule.FloatTransitionOutSeconds),
```

- [ ] **Step 2: Build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: builds successfully (XAML may still have binding issues — Task 13 fixes that).

---

## Task 13: Update the rule editor XAML

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml:1181-1197`

- [ ] **Step 1: Move the transition subpanel out of the `UsesFloatTimedValues` parent**

Find the `StackPanel` block starting at line 1181 that wraps the current Smooth Transition field. The whole block currently sits inside the `UsesFloatTimedValues` parent. Cut the `StackPanel` block (lines 1181-1197) and paste it OUTSIDE that parent, as a sibling of the `UsesFloatTimedValues` panel but still inside the Float Input Mode parent (line 1171's `UsesFloatParameter` block).

Final order inside the Float Input Mode parent:
1. Float Value Mode `ComboBox` + help text (unchanged)
2. **NEW: Transition In / Out subpanel (moved here)**
3. `UsesFloatTimedValues` parent: Active Boost Reward subpanel (was line 1187+, unchanged)
4. `UsesSupporterFloatAdd` parent: Bits/Subs Add subpanel (unchanged)

- [ ] **Step 2: Replace the single TextBox with a UniformGrid of two**

Inside the moved subpanel, replace the existing `TextBlock` and `TextBox` (the "Smooth Transition (seconds)" pair) with:

```xaml
                        <TextBlock Text="{loc:Translate 'Transition In (seconds)'}"
                                   Foreground="{DynamicResource TextBrush}"
                                   FontWeight="SemiBold" />
                        <UniformGrid Columns="2">
                            <StackPanel Margin="0,0,8,0">
                                <TextBlock Text="{loc:Translate 'Transition In (seconds)'}"
                                           Foreground="{DynamicResource TextBrush}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding FloatTransitionInSeconds, UpdateSourceTrigger=LostFocus}" />
                            </StackPanel>
                            <StackPanel Margin="8,0,0,0">
                                <TextBlock Text="{loc:Translate 'Transition Out (seconds)'}"
                                           Foreground="{DynamicResource TextBrush}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding FloatTransitionOutSeconds, UpdateSourceTrigger=LostFocus}" />
                            </StackPanel>
                        </UniformGrid>
                        <TextBlock Margin="0,6,0,0"
                                   Text="{loc:Translate '0 = snap instantly. Higher values glide the value smoothly to and from the target.'}"
                                   Foreground="{DynamicResource MutedBrush}"
                                   TextWrapping="Wrap" />
```

(Adjust spacing values to match the surrounding editor's visual rhythm — the existing fields use `Margin="0,12,0,0"` for top spacing; use the same here.)

- [ ] **Step 3: Build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: builds successfully. Localization will warn about missing keys until Task 14 is done.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml
git commit -m "feat(float): editor shows Transition In and Transition Out as separate fields"
```

---

## Task 14: Add localization keys

**Files:**
- Modify: localization `en-US` JSON source for the editor
- Modify: every `*.extra.json` localization file

- [ ] **Step 1: Add new `en-US` source keys**

In the `en-US` localization source file used by the editor (search for the existing `Smooth Transition (seconds)` key to find the right file), add three new keys:

- `Transition In (seconds)`
- `Transition Out (seconds)`
- `0 = snap instantly. Higher values glide the value smoothly to and from the target.`

- [ ] **Step 2: Update every `.extra.json` file**

For every non-English `*.extra.json` localization file under the project, add matching translations for the three new keys. Use the same conversational register as the surrounding keys (informal `du` for `de-DE`, `tú` for `es-ES`, `tu` for `fr-FR`, etc.). Keep `Crystal Relay`, `OSC`, `VRChat`, and the numeric format placeholders untouched.

- [ ] **Step 3: Drop the old key from `en-US`**

In the same `en-US` source file, remove the `Smooth Transition (seconds)` key (only the one editor used it). If the key is still referenced anywhere else, leave it in place and add a comment noting it is kept for the legacy binding.

- [ ] **Step 4: Run the localization audit**

Run the `LocalizationAudit` project per its existing pattern. Confirm:
- No missing keys
- No empty values
- Placeholders intact
- World-guard message still present in every `.extra.json` file

- [ ] **Step 5: Commit**

```bash
git add localization paths
git commit -m "feat(float): add In/Out transition labels and help text translations"
```

---

## Task 15: Add model field tests for the new derived properties

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs`

- [ ] **Step 1: Add tests for `UsesFloatInTransition` and `UsesFloatOutTransition`**

In the `TriggerRuleFloatModeFieldsTests` class, add:

```csharp
    [Fact]
    public void UsesFloatInTransition_TrueOnlyWhenFloatAndInSecondsPositive()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatTransitionInSeconds = 0.5,
        };
        Assert.True(rule.UsesFloatInTransition);
        rule.FloatTransitionInSeconds = 0;
        Assert.False(rule.UsesFloatInTransition);
    }

    [Fact]
    public void UsesFloatInTransition_FalseWhenParameterTypeIsNotFloat()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Bool,
            FloatTransitionInSeconds = 0.5,
        };
        Assert.False(rule.UsesFloatInTransition);
    }

    [Fact]
    public void UsesFloatOutTransition_TrueOnlyWhenFloatAndOutSecondsPositive()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatTransitionOutSeconds = 0.5,
        };
        Assert.True(rule.UsesFloatOutTransition);
        rule.FloatTransitionOutSeconds = 0;
        Assert.False(rule.UsesFloatOutTransition);
    }
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TriggerRuleFloatModeFieldsTests" --no-restore
```

Expected: all pass.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs
git commit -m "test(float): add coverage for In/Out transition derived properties"
```

---

## Task 16: Run the full test suite

**Files:** (none — verification only)

- [ ] **Step 1: Build the test project**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: builds clean.

- [ ] **Step 2: Run all tests**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: all tests pass. If any fail, fix the model/runtime/test mismatches before continuing.

---

## Task 17: Add CHANGELOG and release record entries

**Files:**
- Modify: `CHANGELOG.txt`
- Modify: `RELEASE-CHANGE-RECORD.txt`

- [ ] **Step 1: Add the CHANGELOG bullet under `v3.1.9 beta 4`**

Open `CHANGELOG.txt`. Under the existing `v3.1.9 beta 4` section, append a new bullet:

```
- Split the Float Smooth Transition into separate Transition In and Transition Out values. The transition now applies to every Float avatar parameter redeem (timed, instant, and all action modes), so values smoothly glide to and from the target instead of snapping.
```

- [ ] **Step 2: Add the internal note to `RELEASE-CHANGE-RECORD.txt`**

In the v3.1.9 working notes section of `RELEASE-CHANGE-RECORD.txt`, add the same item under the appropriate category (Added / Changed) with a slightly more internal-tone description.

- [ ] **Step 3: Commit**

```bash
git add CHANGELOG.txt RELEASE-CHANGE-RECORD.txt
git commit -m "docs: changelog entry for Float Transition In/Out split"
```

---

## Task 18: Final build and manual smoke test plan

**Files:** (none — verification only)

- [ ] **Step 1: Final build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: clean build, zero warnings related to the changes.

- [ ] **Step 2: Launch the debug build**

```bash
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

- [ ] **Step 3: Manual smoke tests (per the spec's test plan)**

Run through each scenario in section 4 of `docs/superpowers/specs/2026-06-23-float-transition-in-out-design.md`:

1. Instant Set with In=1.0 — value glides over 1s.
2. Instant Set with In=0 — value snaps (no behavior change).
3. Timed Set with In=1, Out=2, Active=5 — glides in 1s, holds 5s, glides out 2s.
4. Timed Set with In=0, Out=2 — snaps in, glides out.
5. Add mode with In=0.5 — each add glides over 0.5s.
6. Glitchy with In=0.2, glitch interval=200 — each tick glides over 0.2s.
7. Target equals current — single OSC packet, no ramp.
8. Migrate old save — load a save with `FloatTransitionSeconds=2.0`, confirm it becomes In=2.0, Out=2.0.
9. New save round-trip — save a rule with In=1.0, Out=2.0, reload, confirm values persist.

- [ ] **Step 4: Regression check**

Confirm:
- Existing Float action mode behaviors (no transition) still work.
- Existing Float active boost reward still works.
- Set Trigger, AvatarChange, AvatarRoulet, PlayerMovement, Avatar Scale untouched.
- Bool/Int avatar parameters untouched.

---

## Task 19: GLM 5.2 code review

**Files:** (none — verification only)

- [ ] **Step 1: Dispatch the review**

Dispatch a subagent (or run a manual review session) using GLM 5.2 with this prompt:

> Review the diff for the Float Transition In/Out split feature against the spec at `docs/superpowers/specs/2026-06-23-float-transition-in-out-design.md`. Check:
> 1. Field rename completeness — no remaining references to `FloatTransitionSeconds` in the source tree (except the migration legacy field marked `[JsonIgnore]`).
> 2. Runtime correctness — timed path uses In for ramp-in, Out for ramp-out. New helper handles instant + action modes. Glitchy tick loop ramps to each new value.
> 3. Save migration — old `FloatTransitionSeconds` copies to both In and Out; existing tests cover the migration.
> 4. UI binding — two TextBoxes bound to the new fields, the parent stack panel is the Float Input Mode (not the timed-only parent).
> 5. Build cleanliness — no new warnings, no leftover compile errors.
> 6. Out-of-scope items untouched — Set Trigger, AvatarChange, AvatarRoulet, PlayerMovement, Avatar Scale, Bool, Int, Pulse, SupporterFloatAdd are not changed.
>
> Return a list of any errors, missed requirements, or risks. Be specific (file:line). Do not suggest unrelated refactors.

- [ ] **Step 2: Apply corrections**

For every issue GLM 5.2 reports, apply the correction. If a correction conflicts with the spec, stop and re-open the design discussion with the user.

- [ ] **Step 3: Re-run the build and tests after corrections**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: clean.

- [ ] **Step 4: Final commit (if any corrections landed)**

```bash
git add -A
git commit -m "fix(float): address GLM 5.2 code review findings"
```

---

## Definition of done

- [ ] `dotnet build` clean
- [ ] `dotnet test` all pass
- [ ] Localization audit clean
- [ ] Manual smoke tests from Task 18 all pass
- [ ] GLM 5.2 review complete, corrections applied
- [ ] `CHANGELOG.txt` and `RELEASE-CHANGE-RECORD.txt` updated
- [ ] All changes committed
- [ ] Active build lane is still `beta4` (v3.1.9). If beta 4 is already out by the time the code lands, move the changelog bullet to a new `v3.1.9 beta 5` entry and update `AGENTS.md` accordingly.
