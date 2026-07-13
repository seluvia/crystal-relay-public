# Avatar Swap Return Modes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add two per-rule avatar swap options — Permanent Avatar Change (hide reward after one-time use, no return) and Return to Previous Avatar (stamp current avatar, switch, return to stamped avatar). Both are mutually exclusive radio choices alongside the existing default "Return to Global Return Avatar."

**Architecture:** Three-way radio group in the rule editor XAML → two bools on TriggerRule model → runtime checks in BridgeCoordinator that override the captured return avatar and shared-return-avatar update logic.

**Tech Stack:** C#, WPF/XAML, .NET 10

## Global Constraints

- `PermanentAvatarChange` bool already exists on `TriggerRule.cs:1091` — do not rename or remove
- `ReturnToPreviousAvatar` is a new bool on `TriggerRule` — must be added alongside `PermanentAvatarChange`
- The two bools encode 3 states: neither = Global (default), `ReturnToPreviousAvatar = true` = Previous, `PermanentAvatarChange = true` = Permanent (the `true/true` pair is prevented by UI)
- Radio group goes in `AvatarSwapRuleEditorControl.xaml` after the Avatar Change action badge (~line 1057), before the Active Time section
- Active Time field hides when Permanent is selected
- Permanent mode: after activation, add rule ID to `PermanentChangeCompletedRules` HashSet in BridgeCoordinator
- Return to Previous mode: capture `currentVrChatAvatarId` before the switch, use it as `capturedReturnAvatar`
- Both modes suppress `SetSharedReturnAvatar` (neither updates the global return point)
- On `ResetRuleEffectAsync` for Return to Previous: reset sends to stamped ID, still calls `SetSharedReturnAvatar(avatarResetId, ...)` (same as existing — the reset packet carries the stamped ID, and the return sets it as current)
- On app restart: `PermanentChangeCompletedRules` resets (clean slate — rewards reappear)
- On rule edit/save: remove rule ID from `PermanentChangeCompletedRules`

---

### Task 1: Add ReturnToPreviousAvatar to TriggerRule model

**Files:**
- Modify: `VrcTwitchOscBridge\Models\TriggerRule.cs`

**Interfaces:**
- Produces: `TriggerRule.ReturnToPreviousAvatar` (bool) — new property and backer field

- [ ] **Step 1: Add backer field alongside existing backers (~line 118)**

```csharp
private bool returnToPreviousAvatar;
```
Place after `private bool permanentAvatarChange;` (which is auto-property, so check pattern). Since `PermanentAvatarChange` is an auto-property at line 1091 (no explicit backer), add the new bool as matching auto-property.

```csharp
public bool ReturnToPreviousAvatar { get; set; }
```
Insert at line ~1093 after:
```csharp
public bool PermanentAvatarChange { get; set; }
public bool CooldownOnlyAvatarChange { get; set; }
```
So it becomes:
```csharp
public bool PermanentAvatarChange { get; set; }
public bool ReturnToPreviousAvatar { get; set; }
public bool CooldownOnlyAvatarChange { get; set; }
```

- [ ] **Step 2: Update Clone method (~line 91)**

Find the Clone method and add the new field:
```csharp
ReturnToPreviousAvatar = r.ReturnToPreviousAvatar,
```
Insert after the existing line:
```csharp
PermanentAvatarChange = r.PermanentAvatarChange,
```

- [ ] **Step 3: Build check**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds (new property is not referenced anywhere yet, but model compiles).

---

### Task 2: Update AvatarSwapRuleEditorViewModel

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\AvatarSwapRuleEditorViewModel.cs`

**Interfaces:**
- Consumes: `TriggerRule.ReturnToPreviousAvatar` from Task 1
- Produces: `IsReturnToPrevious`, `IsReturnToGlobal`, `IsPermanent` computed properties; updated `Save()` and `Clone()` that write/read the new bool; updated `IsDirty`

- [ ] **Step 1: Add IsReturnToPrevious property**

After `public bool PermanentAvatarChange { get; set; }` (~line 61), add:
```csharp
public bool ReturnToPreviousAvatar { get; set; }
```

- [ ] **Step 2: Add computed properties for radio group**

After the existing properties (~line 69), add:
```csharp
public bool IsReturnToGlobal => !PermanentAvatarChange && !ReturnToPreviousAvatar;
public bool IsReturnToPrevious => ReturnToPreviousAvatar;
public bool IsPermanent => PermanentAvatarChange;
```

- [ ] **Step 3: Update constructor**

In the constructor, after `PermanentAvatarChange = rule.PermanentAvatarChange;` (~line 21), add:
```csharp
ReturnToPreviousAvatar = rule.ReturnToPreviousAvatar;
```

- [ ] **Step 4: Update IsDirty**

In `IsDirty`, after `PermanentAvatarChange != OriginalSnapshot.PermanentAvatarChange` (~line 44), add:
```csharp
|| ReturnToPreviousAvatar != OriginalSnapshot.ReturnToPreviousAvatar
```

- [ ] **Step 5: Update Save()**

In `Save()`, after `Rule.PermanentAvatarChange = PermanentAvatarChange;` (~line 80), add:
```csharp
Rule.ReturnToPreviousAvatar = ReturnToPreviousAvatar;
```

- [ ] **Step 6: Update Clone()**

In the static `Clone()` method, after `PermanentAvatarChange = r.PermanentAvatarChange,` (~line 100), add:
```csharp
ReturnToPreviousAvatar = r.ReturnToPreviousAvatar,
```

- [ ] **Step 7: Build check**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

---

### Task 3: Add radio group UI to AvatarSwapRuleEditorControl.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\UserControls\AvatarSwapRuleEditorControl.xaml`

**Interfaces:**
- Consumes: `IsReturnToGlobal`, `IsReturnToPrevious`, `IsPermanent` from Task 2
- Consumes: `UsesAvatarChange` (existing binding)

- [ ] **Step 1: Add radio group after the "Avatar Change" badge**

Find the "Avatar Change" badge border at ~line 1043-1057. After the closing `</Border>` at line 1057, insert a new StackPanel for the radio group:

```xml
<StackPanel Margin="0,12,0,0"
            Visibility="{Binding UsesAvatarChange, Converter={StaticResource BoolToVisibilityConverter}}">
    <TextBlock Text="↩ Return Behavior"
               Foreground="{DynamicResource TextBrush}"
               FontWeight="SemiBold" />
    <RadioButton Margin="0,6,0,0"
                 GroupName="AvatarReturnMode"
                 IsChecked="{Binding IsReturnToGlobal}"
                 Content="Return to Global Return Avatar"
                 Foreground="{DynamicResource TextBrush}" />
    <RadioButton Margin="0,4,0,0"
                 GroupName="AvatarReturnMode"
                 IsChecked="{Binding IsReturnToPrevious}"
                 Content="Return to Previous Avatar"
                 Foreground="{DynamicResource TextBrush}" />
    <RadioButton Margin="0,4,0,0"
                 GroupName="AvatarReturnMode"
                 IsChecked="{Binding IsPermanent}"
                 Content="Permanent (No Return)"
                 Foreground="{DynamicResource TextBrush}" />
    <TextBlock Margin="0,6,0,0"
               Text="Will return to the avatar you were wearing before this swap."
               Foreground="{DynamicResource MutedBrush}"
               TextWrapping="Wrap"
               Visibility="{Binding IsReturnToPrevious, Converter={StaticResource BoolToVisibilityConverter}}" />
</StackPanel>
```

- [ ] **Step 2: Wire Active Time field to also hide when Permanent is selected**

The Active Time section at ~line 1736-1771 currently hides when `AvatarChangeCooldownOnlyModeEnabled` is true. Add an additional condition to hide when `IsPermanent` is true.

Update the existing MultiDataTrigger at lines 1742-1749. Add a third condition:
```xml
<Condition Binding="{Binding IsPermanent}" Value="True" />
```
So the full conditions become:
```xml
<MultiDataTrigger.Conditions>
    <Condition Binding="{Binding UsesAvatarChange}" Value="True" />
    <Condition Binding="{Binding DataContext.Settings.AvatarChangeCooldownOnlyModeEnabled, RelativeSource={RelativeSource AncestorType=Window}, FallbackValue=False}" Value="True" />
    <Condition Binding="{Binding IsPermanent}" Value="True" />
</MultiDataTrigger.Conditions>
```
Change the `MultiDataTrigger` to require **any** condition instead of **all** — actually, `MultiDataTrigger` uses AND logic by default. The current behvior is: Active Time is hidden when `UsesAvatarChange && CooldownOnlyModeEnabled`. We want it hidden when either `CooldownOnlyModeEnabled` OR `IsPermanent`.

Replace the `MultiDataTrigger` with a `DataTrigger` for the IsPermanent case, and keep the existing MultiDataTrigger for cooldown-only. Add a second trigger:

```xml
<Style.Triggers>
    <MultiDataTrigger>
        <MultiDataTrigger.Conditions>
            <Condition Binding="{Binding UsesAvatarChange}" Value="True" />
            <Condition Binding="{Binding DataContext.Settings.AvatarChangeCooldownOnlyModeEnabled, RelativeSource={RelativeSource AncestorType=Window}, FallbackValue=False}" Value="True" />
        </MultiDataTrigger.Conditions>
        <Setter Property="Visibility" Value="Collapsed" />
    </MultiDataTrigger>
    <DataTrigger Binding="{Binding IsPermanent}" Value="True">
        <Setter Property="Visibility" Value="Collapsed" />
    </DataTrigger>
</Style.Triggers>
```

- [ ] **Step 3: Build check**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

---

### Task 4: BridgeCoordinator runtime changes

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`

**Interfaces:**
- Consumes: `TriggerRule.ReturnToPreviousAvatar`, `TriggerRule.PermanentAvatarChange` from Task 1
- Produces: `PermanentChangeCompletedRules` field; updated execution flow for both modes

- [ ] **Step 1: Add PermanentChangeCompletedRules field**

Find the field declarations near the top of the class (around the `stateGate` / `cooldowns` area). Add:
```csharp
private readonly HashSet<Guid> permanentChangeCompletedRules = [];
```

- [ ] **Step 2: Update ExecuteRuleActionAsync — detect modes before cooldown-only check**

At line 7538, before the existing `suppressSharedReturnAvatarUpdate` variable, add:
```csharp
var isPermanentAvatarChange = executionRule.ActionType is OscActionType.AvatarChange
    && executionRule.PermanentAvatarChange;
var isReturnToPreviousAvatar = executionRule.ActionType is OscActionType.AvatarChange
    && executionRule.ReturnToPreviousAvatar
    && executionRule.DurationSeconds > 0;
```

After line 7538, add the permanent mode to the suppress condition. Change:
```csharp
var suppressSharedReturnAvatarUpdate = IsCooldownOnlyDirectAvatarChange(rule);
```
to:
```csharp
var suppressSharedReturnAvatarUpdate = IsCooldownOnlyDirectAvatarChange(rule) || isPermanentAvatarChange;
```

After the existing `DurationSeconds = 0` block (~line 7541), add:
```csharp
if (isPermanentAvatarChange)
{
    rule = rule with { DurationSeconds = 0 };
}
```

- [ ] **Step 3: Override capturedReturnAvatar for Return to Previous**

At line 7564, after the existing `capturedReturnAvatar` capture, add:
```csharp
if (isReturnToPreviousAvatar)
{
    var previousAvatarId = GetCurrentVrChatAvatarId();
    capturedReturnAvatar = !string.IsNullOrWhiteSpace(previousAvatarId)
        ? new SharedReturnAvatarSnapshot(previousAvatarId, string.Empty)
        : capturedReturnAvatar;
}
```

- [ ] **Step 4: After SetCurrentVrChatAvatar — register permanent completion + skip SetSharedReturnAvatar**

At line 7804-7817 (the block that calls `SetCurrentVrChatAvatar` and optionally `SetSharedReturnAvatar`), the existing code already skips `SetSharedReturnAvatar` when `suppressSharedReturnAvatarUpdate` is true. Since we added `isPermanentAvatarChange` to that variable, this is already handled.

After this block (after line 7817), add permanent completion tracking:
```csharp
if (!isTest && isPermanentAvatarChange && !string.IsNullOrWhiteSpace(action.AvatarTargetId))
{
    lock (stateGate)
    {
        permanentChangeCompletedRules.Add(rule.Id);
    }
    ManagedRewardAvailabilityChanged?.Invoke();
}
```

- [ ] **Step 5: Schedule activity with previous avatar ID for Return to Previous**

At line 7820-7832, the existing code creates `Payload` inline. Restructure it to a variable and add `previousAvatarId` for Return to Previous:

Replace the avatar-change activity recording block (lines 7820-7832) with:
```csharp
if (!isTest && !isResuming && executionRule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
{
    var payload = new Dictionary<string, object>
    {
        ["avatarTargetId"] = action.AvatarTargetId ?? string.Empty
    };
    if (isReturnToPreviousAvatar)
    {
        payload["previousAvatarId"] = capturedReturnAvatar.AvatarId ?? string.Empty;
    }
    var expiresAt = executionRule.DurationSeconds > 0
        ? DateTimeOffset.UtcNow.AddSeconds(executionRule.DurationSeconds)
        : (DateTimeOffset?)null;
    await activityResumeService.RecordActivityStartedAsync(new ResumeActivity
    {
        Type = ResumeActivityType.AvatarChange,
        RuleId = rule.Id,
        ExpiresAt = expiresAt,
        Payload = payload
    }, action.AvatarTargetId ?? string.Empty);
}
```

- [ ] **Step 6: Add resumePreviousAvatarId parameter to ExecuteRuleActionAsync**

For activity resume to restore Return to Previous swaps correctly, add an optional string parameter to `ExecuteRuleActionAsync`:

```csharp
private async Task ExecuteRuleActionAsync(
    ...existing params...,
    bool isResuming = false,
    string? resumePreviousAvatarId = null)
```

At line 7564, replace the existing `capturedReturnAvatar` capture with:
```csharp
var capturedReturnAvatar = !string.IsNullOrWhiteSpace(resumePreviousAvatarId)
    ? new SharedReturnAvatarSnapshot(resumePreviousAvatarId, string.Empty)
    : (executionRule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
      && executionRule.DurationSeconds > 0
        ? GetSharedReturnAvatarSnapshot()
        : SharedReturnAvatarSnapshot.Empty;
```

- [ ] **Step 7: Update resume handler in TryResumePendingActivitiesAsync**

At line 14692-14708, update the AvatarChange resume case to read `previousAvatarId` from activity payload:

```csharp
case ResumeActivityType.AvatarChange:
{
    var rule = activeConfiguration.Rules.FirstOrDefault(r => r.Id == activity.RuleId);
    if (rule is null)
    {
        return;
    }

    var previousAvatarId = activity.Payload?.GetValueOrDefault("previousAvatarId") as string;

    await ExecuteRuleActionAsync(
        rule,
        null,
        cancellationToken,
        isTest: false,
        queuedReplay: false,
        allowLaneQueue: false,
        isResuming: true,
        resumePreviousAvatarId: previousAvatarId);
    break;
}
```

- [ ] **Step 8: Add ClearAllPermanentChangeCompleted public method on BridgeCoordinator**

```csharp
public void ClearAllPermanentChangeCompleted()
{
    lock (stateGate)
    {
        permanentChangeCompletedRules.Clear();
    }
}
```

- [ ] **Step 9: Wire save callback in MainWindowViewModel**

At `MainWindowViewModel.cs:5472-5477`, update the callback passed to `AvatarSwapManagerViewModel`:

```csharp
var managerVm = new AvatarSwapManagerViewModel(
    Settings,
    this,
    TryGetVrChatAvatarThumbnailUrl,
    () =>
    {
        Coordinator?.ClearAllPermanentChangeCompleted();
        QueueSave(0);
        QueueBridgeRefresh();
        QueueManagedRewardSync(0, ManagedRewardSyncReason.SettingsEdit);
    });
```

- [ ] **Step 10: Handle Return to Previous on reset**

In `ResetRuleEffectAsync` at line 9748-9770, the reset already sends the `avatarResetId` which will be the stamped previous avatar ID (since it was put into the `ResolvedRuleAction.AvatarResetId` via the captured return). The `SetSharedReturnAvatar` call at line 9769 uses this same ID — this is correct behavior (updates shared return avatar to the returned-to avatar). No change needed here.

- [ ] **Step 11: Build check**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

---

### Task 5: Update reward visibility for permanent completion

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs`
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` (call sites)

**Interfaces:**
- Consumes: `PermanentChangeCompletedRules` from Task 4

- [ ] **Step 1: Extend IsRuleActiveForCurrentAvatar signature**

In `BridgeRuntimeConfiguration.cs` at line 1797, add a `permanentChangeCompleted` parameter:
```csharp
public static bool IsRuleActiveForCurrentAvatar(
    bool isGlobalOverride,
    bool belongsToMasterAvatarProfile,
    OscActionType actionType,
    string? avatarChangeTargetId,
    string? requiredAvatarId,
    string? currentAvatarId,
    bool avatarChangeTransitionActive,
    bool avatarChangeCooldownOnlyModeEnabled = false,
    bool permanentAvatarChange = false,
    bool permanentChangeCompleted = false)
```

- [ ] **Step 2: Add permanent check in the method body**

After line 1811, add:
```csharp
if (permanentAvatarChange && permanentChangeCompleted)
{
    return false;
}
```

- [ ] **Step 3: Update all call sites in BridgeCoordinator**

Find all calls to `IsRuleActiveForCurrentAvatar` and add the two new parameters. There are call sites in:
- Line ~2466, 2475 (reward manager methods)
- Line ~3471, 3483 (other visibility checks)
- Line ~6972, 7012, 7055, 7107 (visibility evaluation logic)

For each call site, add:
```csharp
permanentAvatarChange: rule.PermanentAvatarChange,
permanentChangeCompleted: permanentChangeCompletedRules.Contains(rule.Id)
```

(Need to use a different accessor since `permanentChangeCompletedRules` is in BridgeCoordinator. The call sites that are inside BridgeCoordinator can pass the set directly. For external callers via `AvatarRuleActivationPolicy`, wrap in a method.)

Actually, since `IsRuleActiveForCurrentAvatar` is a static utility called from various places including `BridgeCoordinator` methods, the simplest approach is to pass the completed-set check as a bool parameter. For BridgeCoordinator callers, pass `permanentChangeCompletedRules.Contains(rule.Id)`. For external callers, pass `false` as default.

But looking at the actual call sites more carefully:

The existing callers (all in BridgeCoordinator.cs) use patterns like:
```csharp
AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
    ...
    avatarChangeCooldownOnlyModeEnabled: configuration.AvatarChangeCooldownOnlyModeEnabled)
```

For these, add:
```csharp
permanentAvatarChange: rule.PermanentAvatarChange,
permanentChangeCompleted: permanentChangeCompletedRules.Contains(rule.Id)
```

Let me search for all usages of `IsRuleActiveForCurrentAvatar` to find every call site.

- [ ] **Step 4: Add a public helper method on BridgeCoordinator for external callers**

```csharp
public bool IsPermanentChangeCompleted(Guid ruleId)
{
    lock (stateGate)
    {
        return permanentChangeCompletedRules.Contains(ruleId);
    }
}
```

- [ ] **Step 5: Build check**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

---

### Task 6: Final build and smoke test

- [ ] **Step 1: Full build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors.

- [ ] **Step 2: Test project build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Run existing tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: All existing tests pass (no regressions from the bool addition).

---

### Task 7: Localization

**Files:**
- Modify: Locale JSON files for any new UI text added

- [ ] **Step 1: Check what text was added**

From Task 3, the new hardcoded text strings:
- "↩ Return Behavior"
- "Return to Global Return Avatar"
- "Return to Previous Avatar"
- "Permanent (No Return)"
- "Will return to the avatar you were wearing before this swap."

These should use `loc:Translate` bindings. In Step 1 of Task 3, replace the hardcoded text with localization keys:
- `{loc:Translate 'Avatar Return Behavior'}`
- `{loc:Translate 'Return to Global Return Avatar'}`
- `{loc:Translate 'Return to Previous Avatar'}`
- `{loc:Translate 'Permanent No Return'}`
- `{loc:Translate 'Will return to the avatar you were wearing before this swap'}`

- [ ] **Step 2: Add en-US localization entries**

Add the new keys to the en-US locale file with appropriate values.

- [ ] **Step 3: Run localization audit**

Run: `powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\Run-LocalizationAudit.ps1"`
Expected: Audit passes with no missing keys.

- [ ] **Step 4: Final build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.
