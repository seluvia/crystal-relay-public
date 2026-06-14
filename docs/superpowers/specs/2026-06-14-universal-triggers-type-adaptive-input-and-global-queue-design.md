# Universal Triggers — Type-Adaptive Input + Global Queue

**Date:** 2026-06-14
**Lane:** v3.1.10 beta
**Scope:** Universal Triggers editor and runtime. No model, persistence, or Fooma importer changes. No other redeem library touched.

---

## 1. Problem

The current Universal Triggers editor has three real defects that hurt streamers who use a mix of `Bool`, `Int`, `Float`, and `String` OSC params (for example, a Ragdoll toggle that was a `Bool` in a new avatar version):

1. **The value input box is a freeform `TextBox` for every type.** Picking `Bool` in the type dropdown does not change the input — the user still has to type `True` or `False` into a plain text field, and the OSC build step silently accepts whatever string is in there. Picking `Int` or `Float` does not stop the user from typing `abc` and getting a runtime `FormatException`.
2. **The queue is per-trigger only.** `BridgeCoordinator.ExecuteUniversalTriggerAsync` acquires a `SemaphoreSlim(1,1)` keyed on the trigger's `Guid` (line 3927). Two redeems of the *same* trigger serialize; two redeems of *different* triggers run in parallel, so a flood of cross-trigger redemptions can interleave their OSC packets.
3. **Fooma-imported triggers whose `TargetValue`/`DefaultValue` were `0`/`1` come in as `Int`** because the Fooma config stored them as JSON numbers. After import, the user has to retype them by hand, and the current freeform textbox makes that annoying.

The user has confirmed the goal: make the input control adapt to the type, make the queue global so two redeem-with-queue redemptions can never interleave, and leave the Fooma importer alone (the new input makes the retype trivial).

## 2. Goals

- The `Value` (TargetValue) and `Reset to` (DefaultValue) input controls swap to match `ValueKind`: `Bool` → `CheckBox`, `Int` → numeric `TextBox`, `Float` → numeric `TextBox`, `String` → plain `TextBox`.
- Two Universal Trigger redeems that both have `AddToQueue=true` serialize through a single global gate, regardless of whether they target the same trigger or different triggers. `AddToQueue=false` continues to mean "run in parallel".
- A streamer can re-type an imported `Int 0/1` action into a `Bool true/false` action in one click (pick `Bool` in the dropdown, the input becomes a `CheckBox`).
- All three changes are isolated to the Universal Triggers editor and runtime. No data model, no persistence format, no Fooma importer, no Twitch reward sync, no other redeem library, no chatbox, no theme system.

## 3. Non-Goals

- No change to `UniversalTriggerRule`, `UniversalTriggerAction`, `UniversalTriggerType`, `UniversalTriggerValueKind`, or any other model.
- No change to `FoomaInteractionConfigImporter`, `UniversalTriggerFusionService`, `SettingsStore`, the `PersistedProfileSettings` DTO, or the migrator chain.
- No change to the Twitch reward sync, Fire Sale, Avatar Sets, Avatar Scaling, Movement, Power-Ups, Cash Payments, Bits + Subs overrides, or the chatbox.
- No new top-level tab or nav change.
- No new converter outside the three required for the new input controls.
- No new theme brush, theme style, or localization key. (The controls themselves do not need new labels — the existing `ValueKind` dropdown already labels the type.)
- No new persisted property, no migration, no version bump.
- No "Heal from current avatar" button (out of scope; the user can re-type via the new UI).
- No max-parallel knob on the global gate (out of scope; the user asked for binary on/off).

## 4. Architecture

Three changes, three files (plus one new test project):

| File | Change |
|---|---|
| `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs` (new) | Three pure `IValueConverter`s: `UniversalTriggerBoolConverter`, `UniversalTriggerIntConverter`, `UniversalTriggerFloatConverter`. The `String` case uses the built-in direct binding (no converter). The visibility swap reuses the existing `EnumToVisibilityConverter` in `Converters.cs` (the converter already compares `value.ToString()` to `parameter.ToString()` and returns `Visible`/`Collapsed`, which is exactly what we need). |
| `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml` | Swap the single freeform `TargetValue` / `DefaultValue` `TextBox` pair for a 4-control `Grid` whose `Visibility` is data-triggered on `ValueKind`. Add the three converters as `Window.Resources` so the existing `DataTemplate` can resolve them. |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | Add a single `SemaphoreSlim(1, 1)` field `universalTriggerGlobalGate`. In `ExecuteUniversalTriggerAsync`, when `shouldQueue` is true, acquire the global gate *outside* the per-trigger gate. Reset / clear both gates in the existing stop/dispose path. |
| `VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj` (new) + `Converters/UniversalTriggerValueConvertersTests.cs` (new) | Lightweight `Microsoft.NET.Sdk` test project targeting `net10.0-windows`. xUnit. Tests only the three converters — they are the only piece of this change with non-trivial logic. |

The new tests project is allowed because: (a) `AGENTS.md` does not forbid test projects; (b) the three converters are pure and self-contained; (c) without tests we would be guessing about the invariant-culture parsing, the empty-input behavior, and the bool normalization rules.

### 4.1 Why a separate test project

The main `VrcTwitchOscBridge.csproj` is a WPF app. Mixing a test target into a WPF app is awkward (you need a different `OutputType`, different `UseWPF` settings, etc.) and `AGENTS.md` says the project has `EnableDefaultCompileItems=false`. A sibling `VrcTwitchOscBridge.Tests` project keeps the converter logic unit-testable in seconds without polluting the main app's build graph. It is built only by `dotnet test`; the main app's build script is unchanged.

## 5. Converters

All three converters live in `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs` and implement `IValueConverter` directly. They use `CultureInfo.InvariantCulture` for all parsing and formatting (matches the existing OSC build path in `VrChatOscClient.BuildPacket`).

### 5.1 `UniversalTriggerBoolConverter`

`Convert(string? value, targetType, parameter, culture) -> bool`

- `null`, `""`, `"False"`, `"false"`, `"0"` → `false` (the CheckBox defaults to unchecked; the OSC send will be `false` if the user has not set a value yet)
- `"True"`, `"true"`, `"1"` → `true`
- any other string → `false` (safe default; user can fix the value in the `CheckBox`)

`ConvertBack(bool? value, ...) -> string`

- `true` → `"True"`
- `false` → `"False"`
- `null` → `"False"`

Rationale for `"True"`/`"False"` (capitalized, OSC literal form): matches the form `VrChatOscClient.ParseBoolean` accepts via `bool.TryParse`, and matches the form `FoomaInteractionConfigImporter.JsonValueToText` already writes for booleans. Single canonical form on disk. Empty input on a brand-new action maps to `"False"` so a freshly created `Bool` action has a known-good OSC value.

### 5.2 `UniversalTriggerIntConverter`

`Convert(string? value, ...) -> string`

- `null` / `""` → `"0"` (a brand-new `Int` action has a known-good OSC value; the `TextBox` always shows a number)
- parses as `int` invariant → format with `int.ToString(CultureInfo.InvariantCulture)` → returns the canonical string
- parses as `double` invariant that is an exact integer → returns `((int)d).ToString(CultureInfo.InvariantCulture)` (so `"1.0"` becomes `"1"`, so a previously-Float action re-typed as Int does not keep the `.0`)
- unparseable → returns the *original* string unchanged (so the user does not lose what they typed; the OSC send will fail later with a clear `FormatException`, same as today)

`ConvertBack(string? value, ...) -> int`

- empty / `null` → `0` (matches the "0" default for a blank int field; safe at OSC send time)
- parses as `int` invariant → returns the int
- unparseable → returns `0` (no exceptions thrown — the OSC send will surface the bad value, and the field is highlighted by `ValidatesOnExceptions=True` + a red border style on the `TextBox`)

The "clear field shows `0`" behavior is intentional: int fields always show a number. `ConvertBack` does not throw by design: throwing would crash the `Binding` pipeline and freeze the editor. Validation is surfaced through the `Text` going red, not through exceptions.

### 5.3 `UniversalTriggerFloatConverter`

`Convert(string? value, ...) -> string`

- `null` / `""` → `"0"` (a brand-new `Float` action has a known-good OSC value; the `TextBox` always shows a number)
- parses as `double` invariant → format with `double.ToString("R", CultureInfo.InvariantCulture)` → round-trips safely
- exact-integer doubles (e.g. `1.0`) are formatted as `"1"` (no phantom `.0`), so a previously-Int action re-typed as Float does not gain a phantom decimal
- unparseable → returns the *original* string unchanged (so the user does not lose what they typed; the OSC send will fail later with a clear `FormatException`, same as today)

`ConvertBack(string? value, ...) -> double`

- empty / `null` → `0.0`
- parses as `double` invariant → returns the double
- unparseable → returns `0.0`

### 5.4 Behavior on `ValueKind` change

When the user changes the `ValueKind` dropdown, the input control swaps. The existing `TargetValue` string is passed through the new converter's `Convert`:

- `Bool → Int`: `"True"` / `"False"` do not parse as int, so the field shows the *original* unparseable string for one frame. On the user's next edit, `ConvertBack` rewrites it to `"0"`. The intermediate state is acceptable for an explicit type change; the user is telling the app "switch to int", so seeing the old value then a clean `0` is fine.
- `Int → Float`: `"5"` round-trips as `"5"` (not `"5.0"`, per §5.3).
- `Float → Int`: `"5.5"` round-trips as `"5.5"` (unparseable as int), then is rewritten to `"0"` on the next interaction. Acceptable for an explicit type change.
- `Int → String` / `Float → String`: no coercion needed; the string is unchanged.
- `String → Int/Float/Bool`: parsed if possible, else the field shows the original string and the OSC send will surface a clear error at send time.

The `DefaultValue` field gets the exact same behavior. No model change is required to support this — the converters do all the work.

## 6. XAML changes

Inside the OSC action row `DataTemplate` in `UniversalTriggersManagerWindow.xaml` (current lines 1563-1569), the `UniformGrid` 3-column block is replaced with a 2-column `Grid`:

- Column 0: `ValueKind` `ComboBox` (unchanged).
- Column 1: a `Grid` with four stacked children, each `Visibility` bound via a `DataTrigger` on `ValueKind`:

```xaml
<Grid Grid.Column="1">
    <CheckBox Content="{loc:Translate 'On'}"
              IsChecked="{Binding TargetValue, Converter={StaticResource UniversalTriggerBoolConverter}, Mode=TwoWay}"
              Visibility="{Binding ValueKind, Converter={StaticResource ValueKindToVisibility}, ConverterParameter=Bool}" />
    <TextBox Text="{Binding TargetValue, Converter={StaticResource UniversalTriggerIntConverter}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
             Visibility="{Binding ValueKind, Converter={StaticResource ValueKindToVisibility}, ConverterParameter=Int}" />
    <TextBox Text="{Binding TargetValue, Converter={StaticResource UniversalTriggerFloatConverter}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
             Visibility="{Binding ValueKind, Converter={StaticResource ValueKindToVisibility}, ConverterParameter=Float}" />
    <TextBox Text="{Binding TargetValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
             Visibility="{Binding ValueKind, Converter={StaticResource ValueKindToVisibility}, ConverterParameter=String}" />
</Grid>
```

A new `ValueKindToVisibilityConverter` is *not* needed — the existing `EnumToVisibilityConverter` in `VrcTwitchOscBridge/Converters.cs` (line 33) already does exactly the right thing: it returns `Visible` when `value.ToString() == parameter.ToString()` (case-insensitive), else `Collapsed`. We bind `Visibility="{Binding ValueKind, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=Bool}"` (and `Int`, `Float`, `String` for the other three).

The `DefaultValue` field gets the exact same treatment, with the same four controls and a parallel `Visibility` binding on `ValueKind`. The column ordering of the action row becomes: `[OscAddress] [ValueKind] [Value] [Reset to] [Queue/Duration row]`.

The action row gets a small `ColumnDefinitions` change on the outer `Grid`:
- `*` for `OscAddress` (column 0)
- `140` for `ValueKind` (column 1)
- `*` for `Value` (column 2)
- `*` for `Reset to` (column 3)
- `Auto` for the `Queue` / `Duration` row (column 4, unchanged)

Min widths on `Value` and `Reset to` are `80` and `120` so a `CheckBox` does not collapse.

Localization: the new `On` label is the only new user-facing string. It is added to `en-US.extra.json` and to every other `*.extra.json` localization file with the informal-register translations. The localization audit is re-run as part of this change.

## 7. Global queue

`BridgeCoordinator.cs` gets one new field and one new block in `ExecuteUniversalTriggerAsync`.

### 7.1 New field

```csharp
private readonly SemaphoreSlim universalTriggerGlobalGate = new(1, 1);
```

Placed next to the existing `private readonly Dictionary<Guid, SemaphoreSlim> universalTriggerQueueGates = [];` field at line 179. No dictionary — a single instance is enough.

### 7.2 New flow in `ExecuteUniversalTriggerAsync`

Current code (lines 3910-3946):

```csharp
var shouldQueue = actions.Any(action => action.AddToQueue);
if (shouldQueue)
{
    var gate = GetUniversalTriggerQueueGate(trigger.Id);
    await gate.WaitAsync(cancellationToken);
    try { await ExecuteUniversalActionsAsync(trigger, actions, cancellationToken); }
    finally { gate.Release(); }
}
else
{
    await ExecuteUniversalActionsAsync(trigger, actions, cancellationToken);
}
```

New code:

```csharp
var shouldQueue = actions.Any(action => action.AddToQueue);
if (shouldQueue)
{
    await universalTriggerGlobalGate.WaitAsync(cancellationToken);
    try
    {
        var gate = GetUniversalTriggerQueueGate(trigger.Id);
        await gate.WaitAsync(cancellationToken);
        try { await ExecuteUniversalActionsAsync(trigger, actions, cancellationToken); }
        finally { gate.Release(); }
    }
    finally { universalTriggerGlobalGate.Release(); }
}
else
{
    await ExecuteUniversalActionsAsync(trigger, actions, cancellationToken);
}
```

The global gate is acquired first and released last. The per-trigger gate is unchanged. Two redeems of different triggers with `AddToQueue=true` now serialize through the global gate; two redeems of the same trigger still serialize through the per-trigger gate (and through the global gate, which is a no-op the second time they would be inside the same chain). `AddToQueue=false` triggers continue to run in parallel.

### 7.3 Stop / dispose path

The current stop/dispose path at line 17288 snapshots `universalTriggerQueueGates` and clears the dictionary. The new global gate has no dictionary snapshot — the existing `DisposeAsync` / stop logic for `BridgeCoordinator` already drains the gate naturally through the `CancellationToken` propagation. We do not need to add `Wait()` on the gate in stop, but we do add a single `universalTriggerGlobalGate.Dispose()` to the same `Dispose` block that already disposes the per-trigger gates. (No behavior change in shutdown; the `Dispose` block runs after the service has already stopped accepting new redeems.)

## 8. Fooma importer

No change. `FoomaInteractionConfigImporter.ResolveValueKind` is correct as written: it maps `JsonValueKind.True/False → Bool`, integer `Number → Int`, non-integer `Number → Float`, everything else → `String`. A user who imports a Fooma config that wrote `"TargetValue": 1` and `"DefaultValue": 0` will see those actions as `Int` — which is what the Fooma config actually says.

The new type-adapting input makes the retype trivial: change `ValueKind` from `Int` to `Bool` in the dropdown, and the input swaps to a `CheckBox`. The user does not type `True`/`False`; they click.

## 9. Testing

A new `VrcTwitchOscBridge.Tests` project is added at the solution root next to `VrcTwitchOscBridge`. It targets `net10.0-windows`, references `VrcTwitchOscBridge.csproj`, and uses xUnit.

`Converters/UniversalTriggerValueConvertersTests.cs` covers the three converters:

- **Bool converter:** `"True"` / `"true"` / `"1"` → `true`; `""` / `null` / `"False"` / `"false"` / `"0"` / `"abc"` → `false`. `ConvertBack(true) == "True"`, `ConvertBack(false) == "False"`, `ConvertBack(null) == "False"`. The string form stored on disk is always `"True"` or `"False"`.
- **Int converter:** `Convert` of `null` / `""` returns `"0"`. Round-trip `0`, `1`, `-1`, `2147483647` survives. `"1.0"` is canonicalized to `"1"`. Unparseable input (`"abc"`) survives `Convert` unchanged; `ConvertBack("abc")` returns `0` without throwing.
- **Float converter:** `Convert` of `null` / `""` returns `"0"`. Round-trip `0.0`, `1.5`, `-1.5`, `0.0001` survives. `Convert` of `"1.0"` returns `"1"` (no phantom `.0`). Unparseable input survives `Convert` unchanged; `ConvertBack("abc")` returns `0.0` without throwing.

`Build-Crystal-Relay-Test.ps1` and the main `dotnet build` are unchanged. The tests project is built only by `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"`.

## 10. File changes

### 10.1 New files

| File | Purpose |
|---|---|
| `VrcTwitchOscBridge/Converters/UniversalTriggerValueConverters.cs` | Three `IValueConverter`s: `UniversalTriggerBoolConverter`, `UniversalTriggerIntConverter`, `UniversalTriggerFloatConverter`. Pure, no UI dependencies. The visibility swap reuses the existing `EnumToVisibilityConverter` from `VrcTwitchOscBridge/Converters.cs`. |
| `VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj` | xUnit test project. |
| `VrcTwitchOscBridge.Tests/Converters/UniversalTriggerValueConvertersTests.cs` | xUnit tests for the three converters. |

### 10.2 Edited files

| File | Change |
|---|---|
| `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml` | Replace the two `TextBox` bindings for `TargetValue` / `DefaultValue` with a 4-control `Grid` whose `Visibility` is data-triggered on `ValueKind`. Add the converters and the new `loc:Translate On` key in `Window.Resources`. |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | Add `universalTriggerGlobalGate` field. Wrap the per-trigger `WaitAsync` in the global `WaitAsync`. Dispose the global gate in the existing `Dispose` block. |
| `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` | Add an explicit `<Compile Include="Converters\UniversalTriggerValueConverters.cs" />` entry, because `EnableDefaultCompileItems=false` means new `.cs` files are not picked up automatically (per `AGENTS.md` "Project File Rules"). |
| `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` | Add `Universal Triggers Value On` → `On`. |
| `VrcTwitchOscBridge/Resources/Localization/*.extra.json` (every other language) | Add matching placeholder translations for the new key. |

### 10.3 Files NOT touched

- `Models/UniversalTriggerRule.cs`, `Models/UniversalTriggerAction.cs`, `Models/UniversalTriggerType.cs`, `Models/UniversalTriggerValueKind.cs`.
- `Services/UniversalTriggerFusionService.cs`, `Services/FoomaInteractionConfigImporter.cs`, `Services/SettingsStore.cs`.
- `Services/VrChatOscClient.cs` (the OSC packet builder is unchanged).
- `Services/BridgeRuntimeConfiguration.cs` (the snapshot types are unchanged).
- `ThemeManager.cs`, any other theme palette, any other redeem library, the Twitch reward sync code, the chatbox, the about page.

## 11. Localization

One new key:

- `Universal Triggers Value On` → `On`

The label appears next to the `CheckBox` for `Bool` actions, e.g. `☐ On` / `☑ On`. Translations for every existing `*.extra.json` language file use the informal register, keep `Bool`/`Int`/`Float`/`String` in English (they are already kept in English by the type-picker dropdown), and stay conversational. The localization audit is re-run as part of the acceptance check.

## 12. Acceptance criteria

- Open the Universal Triggers editor on a trigger with a `Bool` action. The `Value` input is a `CheckBox` labeled `On`. The `Reset to` input is a `CheckBox` labeled `On`. Toggling the `CheckBox` flips the stored `TargetValue` between `True` and `False` in the model.
- Change the `ValueKind` dropdown to `Int`. The `CheckBox` disappears; a `TextBox` appears. Typing `1` round-trips as `1`. Typing `abc` is left in the field and the `TextBox` border goes red via `ValidatesOnExceptions=True` + a validation error style.
- Change `ValueKind` to `Float`. The `TextBox` now accepts `1.5` round-trips as `1.5`. `1.0` round-trips as `1`. `abc` is left in the field.
- Change `ValueKind` to `String`. The `TextBox` is the original freeform one.
- Redeem the same trigger twice in quick succession with `AddToQueue=true`. The two redeems serialize through the per-trigger gate and through the global gate (verified by `WriteLog` timestamps in the log file).
- Redeem two *different* triggers with `AddToQueue=true` on each. The two redeems serialize through the global gate, in arrival order, even though the per-trigger gates are different. (Verified by `WriteLog` timestamps.)
- Redeem a trigger with `AddToQueue=false` while a `AddToQueue=true` trigger is mid-execution. The `AddToQueue=false` redeem runs in parallel (no global gate acquired).
- `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore` succeeds.
- `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"` succeeds with all converter tests green.
- The localization audit reports zero untranslated keys for `Universal Triggers Value On` across all `*.extra.json` files.
- The Fooma import flow is unchanged: importing a Fooma config that has `"TargetValue": 1` still produces `Int` actions; the new input makes the retype trivial.
- No secrets, tokens, runtime state, or user-local paths are added to the repo.

## 13. Out of scope

- Any change to the data model (`UniversalTriggerRule`, `UniversalTriggerAction`, related enums).
- Any change to the Fooma importer, fusion service, settings persistence, or runtime OSC build path.
- Any change to the Twitch reward sync code.
- Any change to the chatbox, Avatar Sets, Avatar Scaling, Movement, Power-Ups, Cash Payments, Bits + Subs overrides, or the about page.
- Any new top-level tab or nav change.
- Any new converter outside the three listed in §5.
- Any new theme brush, theme style, or persisted property.
- Any "Heal from current avatar" feature.
- Any tunable max-parallel knob on the global gate.
- Any change to the OSC packet builder in `VrChatOscClient` (it already accepts the strings the converters produce).
- Re-importing the user's actual Fooma `Config.json` from `C:\Users\screm\Downloads\` into the repo.
