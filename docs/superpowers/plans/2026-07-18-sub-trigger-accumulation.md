# Sub Trigger Accumulation & Threshold System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dedicated sub-trigger system for avatar swap/roulette rules with configurable threshold count, optional accumulation across events, and optional carryover of excess.

**Architecture:** Three new fields on `TriggerRule`, updated `TriggerRuleSnapshot`, a `Dictionary<Guid,int>` accumulator in `BridgeCoordinator`, and a new XAML editor section replacing the generic "Minimum Amount" section for subscriptions.

**Tech Stack:** C# WPF .NET 10, no new packages.

## Global Constraints

- `TriggerRule` is an `ObservableObject` with backing fields and `SetProperty` for INPC
- `TriggerRuleSnapshot` is a positional record with `FromRule(TriggerRule)` factory
- All new fields must be serializable via System.Text.Json (use `[JsonPropertyName]` or auto-property convention)
- Follow existing XAML patterns: `BoolToVisibilityConverter`, `DynamicResource` brushes, `loc:Translate` for localized strings
- Localization: add keys to `en-US.json` + `en-US.extra.json`; all non-English files get translations added in a separate pass

---
### Task 1: Add Model Fields and Computed Properties to TriggerRule

**Files:**
- Modify: `VrcTwitchOscBridge\Models\TriggerRule.cs`

**Interfaces:**
- Produces: Fields `SubsTriggerCount` (int, default 1), `SubsAccumulationEnabled` (bool, default false), `SubsCarryOverEnabled` (bool, default false)
- Produces: Computed `UsesSubsTriggerSettings` (bool), `SubsTriggerSummary` (string)
- Produces: Updated `TriggerSummary` to use `SubsTriggerCount` for subs
- Produces: Updated `SupporterTimeSettingsSummary` for sub trigger info

- [ ] **Step 1: Add three backing fields after line 155 (after `supporterAvatarScopeLabel`)**

```csharp
private int subsTriggerCount = 1;
private bool subsAccumulationEnabled;
private bool subsCarryOverEnabled;
```

- [ ] **Step 2: Add properties after `IsGiftSubscription` (after line 1090)**

Around line 1090, add after `IsGiftSubscription`:

```csharp
public int SubsTriggerCount
{
    get => subsTriggerCount;
    set
    {
        var normalizedValue = Math.Max(1, value);
        if (SetProperty(ref subsTriggerCount, normalizedValue))
        {
            RaisePropertyChanged(nameof(TriggerSummary));
            RaisePropertyChanged(nameof(SubsTriggerSummary));
        }
    }
}

public bool SubsAccumulationEnabled
{
    get => subsAccumulationEnabled;
    set
    {
        if (SetProperty(ref subsAccumulationEnabled, value))
        {
            RaisePropertyChanged(nameof(TriggerSummary));
            RaisePropertyChanged(nameof(SubsTriggerSummary));
            RaisePropertyChanged(nameof(SubsTriggerSummary));
        }
    }
}

public bool SubsCarryOverEnabled
{
    get => subsCarryOverEnabled;
    set
    {
        if (SetProperty(ref subsCarryOverEnabled, value))
        {
            RaisePropertyChanged(nameof(TriggerSummary));
            RaisePropertyChanged(nameof(SubsTriggerSummary));
        }
    }
}
```

- [ ] **Step 3: Add computed properties near `UsesAmountThreshold` (after line 1586)**

```csharp
public bool UsesSubsTriggerSettings => TriggerType == TwitchTriggerType.Subscriptions;

public string SubsTriggerSummary
{
    get
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(TF("Trigger: {0} subs", Math.Max(1, SubsTriggerCount)));
        if (SubsAccumulationEnabled)
        {
            sb.Append(", ");
            sb.Append(T("accumulate ON"));
            if (SubsCarryOverEnabled)
            {
                sb.Append(", ");
                sb.Append(T("carryover ON"));
            }
        }
        else
        {
            sb.Append(", ");
            sb.Append(T("accumulate OFF"));
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Update `TriggerSummary` for subscription lines (around lines 1953-1956)**

Replace the `TwitchTriggerType.Subscriptions` branch:

Old (line 1953-1956):
```csharp
TwitchTriggerType.Subscriptions when UsesActiveSupporterFloatAdd => SupporterFloatAddSummary,
TwitchTriggerType.Subscriptions => (AmountScaledDurationEnabled || AddBitsToSwapTime)
    ? TF("Subs >= {0} (T1 {1}s, T2 {2}s, T3 {3}s)", Math.Max(1, MinimumAmount), Math.Max(1, SubscriptionTier1SecondsPerSub), Math.Max(1, SubscriptionTier2SecondsPerSub), Math.Max(1, SubscriptionTier3SecondsPerSub))
    : TF("Subs >= {0}", Math.Max(1, MinimumAmount)),
```

New:
```csharp
TwitchTriggerType.Subscriptions when UsesActiveSupporterFloatAdd => SupporterFloatAddSummary,
TwitchTriggerType.Subscriptions when SubsAccumulationEnabled => (AmountScaledDurationEnabled || AddBitsToSwapTime)
    ? TF("Accumulate {0} subs (T1 {1}s, T2 {2}s, T3 {3}s)", Math.Max(1, SubsTriggerCount), Math.Max(1, SubscriptionTier1SecondsPerSub), Math.Max(1, SubscriptionTier2SecondsPerSub), Math.Max(1, SubscriptionTier3SecondsPerSub))
    : TF("Accumulate {0} subs", Math.Max(1, SubsTriggerCount)),
TwitchTriggerType.Subscriptions => (AmountScaledDurationEnabled || AddBitsToSwapTime)
    ? TF("Subs >= {0} (T1 {1}s, T2 {2}s, T3 {3}s)", Math.Max(1, SubsTriggerCount), Math.Max(1, SubscriptionTier1SecondsPerSub), Math.Max(1, SubscriptionTier2SecondsPerSub), Math.Max(1, SubscriptionTier3SecondsPerSub))
    : TF("Subs >= {0}", Math.Max(1, SubsTriggerCount)),
```

- [ ] **Step 5: Update `TriggerType` setter's raises (line 200-221) to include new computed properties**

Add to the `TriggerType` setter's `RaisePropertyChanged` calls (around line 219):
```csharp
RaisePropertyChanged(nameof(UsesSubsTriggerSettings));
RaisePropertyChanged(nameof(SubsTriggerSummary));
```

### Task 2: Update TriggerRuleSnapshot

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs`

**Interfaces:**
- Consumes: `TriggerRule.SubsTriggerCount`, `TriggerRule.SubsAccumulationEnabled`, `TriggerRule.SubsCarryOverEnabled`
- Produces: `TriggerRuleSnapshot.SubsTriggerCount`, `TriggerRuleSnapshot.SubsAccumulationEnabled`, `TriggerRuleSnapshot.SubsCarryOverEnabled`

- [ ] **Step 1: Add three new parameters to `TriggerRuleSnapshot` record (after `MaxAccumulatedDurationSeconds` on line 93)**

Add after line 93:
```csharp
    int SubsTriggerCount = 1,
    bool SubsAccumulationEnabled = false,
    bool SubsCarryOverEnabled = false,
```

- [ ] **Step 2: Add to `FromRule` factory (after the `MaxAccumulatedDurationSeconds` line around line 188)**

Add after the `MaxAccumulatedDurationEnabled`/`MaxAccumulatedDurationSeconds` lines:
```csharp
            SubsTriggerCount: Math.Max(1, rule.SubsTriggerCount),
            SubsAccumulationEnabled: rule.SubsAccumulationEnabled,
            SubsCarryOverEnabled: rule.SubsCarryOverEnabled,
```

### Task 3: Accumulator and Runtime Logic in BridgeCoordinator

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`

**Interfaces:**
- Consumes: `TriggerRuleSnapshot.SubsTriggerCount`, `TriggerRuleSnapshot.SubsAccumulationEnabled`, `TriggerRuleSnapshot.SubsCarryOverEnabled`
- Produces: Modified `SelectSubscriptionMatchingRules` that handles both accumulation and non-accumulation rules

- [ ] **Step 1: Add `_subsAccumulator` dictionary field in BridgeCoordinator**

Find the field declarations area and add:
```csharp
private readonly Dictionary<Guid, int> _subsAccumulator = new();
```

- [ ] **Step 2: Rewrite `SelectSubscriptionMatchingRules` (lines 14002-14034)**

Replace the entire method:

```csharp
private TriggerRuleSnapshot[] SelectSubscriptionMatchingRules(
    IReadOnlyList<TriggerRuleSnapshot> rules,
    int amount,
    string currentAvatarId)
{
    var nonAccumulationRules = rules
        .Where(r => !r.SubsAccumulationEnabled)
        .ToArray();
    var accumulationRules = rules
        .Where(r => r.SubsAccumulationEnabled)
        .ToArray();

    var results = new List<TriggerRuleSnapshot>();

    // --- Non-accumulation path: existing best-threshold matching using SubsTriggerCount ---
    if (nonAccumulationRules.Length > 0)
    {
        var currentAvatarRules = nonAccumulationRules
            .Where(rule => IsSupporterRuleScopedToCurrentAvatar(rule, currentAvatarId))
            .ToArray();
        var currentAvatarMatch = SelectBestSubscriptionThresholdMatch(currentAvatarRules, amount);
        if (currentAvatarMatch is not null)
        {
            results.Add(currentAvatarMatch);
        }
        else
        {
            var globalRules = nonAccumulationRules
                .Where(IsGlobalSupporterRule)
                .ToArray();
            var overrideMatch = SelectBestSubscriptionThresholdMatch(
                globalRules.Where(IsAvatarChangeOverrideRule).ToArray(),
                amount);
            if (overrideMatch is not null)
            {
                results.Add(overrideMatch);
            }
            else
            {
                var fallback = SelectBestSubscriptionThresholdMatch(
                    globalRules.Where(rule => !IsAvatarChangeOverrideRule(rule)).ToArray(),
                    amount);
                if (fallback is not null)
                {
                    results.Add(fallback);
                }
            }
        }
    }

    // --- Accumulation path: feed all accumulation rules and check thresholds ---
    if (accumulationRules.Length > 0)
    {
        foreach (var rule in accumulationRules)
        {
            if (!_subsAccumulator.TryGetValue(rule.Id, out var accumulator))
            {
                accumulator = 0;
            }

            accumulator += amount;

            var cap = Math.Max(1, rule.SubsTriggerCount) * 10;
            if (accumulator > cap)
            {
                accumulator = cap;
            }

            if (accumulator >= Math.Max(1, rule.SubsTriggerCount))
            {
                results.Add(rule);

                if (rule.SubsCarryOverEnabled)
                {
                    accumulator -= Math.Max(1, rule.SubsTriggerCount);
                }
                else
                {
                    accumulator = 0;
                }
            }

            _subsAccumulator[rule.Id] = accumulator;
        }
    }

    return results.ToArray();
}
```

- [ ] **Step 3: Add `SelectBestSubscriptionThresholdMatch` helper method**

Add after the `SelectSubscriptionMatchingRules` method:

```csharp
private static TriggerRuleSnapshot? SelectBestSubscriptionThresholdMatch(
    IReadOnlyList<TriggerRuleSnapshot> rules,
    int amount)
{
    TriggerRuleSnapshot? bestMatch = null;
    var bestThreshold = int.MinValue;

    foreach (var rule in rules)
    {
        var threshold = Math.Max(1, rule.SubsTriggerCount);
        if (amount < threshold)
        {
            continue;
        }

        if (threshold > bestThreshold)
        {
            bestThreshold = threshold;
            bestMatch = rule;
        }
    }

    return bestMatch;
}
```

- [ ] **Step 4: Update the `SelectMatchingRules` dispatch (line 13838)**

Change line 13841 from `bridgeEvent.Amount,` to pass additional context if needed, but the current signature already works since `SelectSubscriptionMatchingRules` is now an instance method (it accesses `_subsAccumulator`). Change the method call from `static` to instance — remove the `static` keyword from `SelectSubscriptionMatchingRules`.

### Task 4: UI XAML — New Subscription Trigger Settings Section

**Files:**
- Modify: `VrcTwitchOscBridge\UserControls\AvatarSwapRuleEditorControl.xaml`

**Interfaces:**
- Consumes: `TriggerRule.UsesSubsTriggerSettings`, `TriggerRule.SubsTriggerCount`, `TriggerRule.SubsAccumulationEnabled`, `TriggerRule.SubsCarryOverEnabled`

- [ ] **Step 1: Modify the "Minimum Amount" section visibility (around lines 958-987)**

Change the visibility binding from `UsesAmountThreshold` to hide it when the trigger is Subscriptions:

Old:
```xml
Visibility="{Binding UsesAmountThreshold, Converter={StaticResource BoolToVisibilityConverter}}"
```

New — add an additional condition to hide when subs trigger settings are active:
```xml
<StackPanel.Style>
    <Style TargetType="StackPanel">
        <Setter Property="Visibility" Value="Collapsed" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding UsesAmountThreshold}" Value="True">
                <Setter Property="Visibility" Value="Visible" />
            </DataTrigger>
            <DataTrigger Binding="{Binding UsesSubsTriggerSettings}" Value="True">
                <Setter Property="Visibility" Value="Collapsed" />
            </DataTrigger>
        </Style.Triggers>
    </Style>
</StackPanel.Style>
```

- [ ] **Step 2: Add the new "Subscription Trigger Settings" section**

Add this after the Minimum Amount stack panel (after line 987), before the "Bits/Subs Amount Timer" section (line 989):

```xml
<StackPanel Margin="0,10,0,0"
            Visibility="{Binding UsesSubsTriggerSettings, Converter={StaticResource BoolToVisibilityConverter}}">
    <Border Background="{DynamicResource PanelSecondaryBrush}"
            BorderBrush="{DynamicResource InputBorderBrush}"
            BorderThickness="1"
            CornerRadius="14"
            Padding="12">
        <StackPanel>
            <TextBlock Text="{loc:Translate 'Subscription Trigger Settings'}"
                       Foreground="{DynamicResource TextBrush}"
                       FontWeight="SemiBold"
                       FontSize="14" />
            <StackPanel Margin="0,10,0,0">
                <TextBlock Text="{loc:Translate 'Subs to Trigger'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold" />
                <TextBox Text="{Binding SubsTriggerCount, UpdateSourceTrigger=PropertyChanged}" />
                <TextBlock Margin="0,6,0,0"
                           Foreground="{DynamicResource MutedBrush}"
                           TextWrapping="Wrap"
                           Text="{loc:Translate 'How many subs are needed before this rule fires. Gift subs count by the total gifted.'}" />
            </StackPanel>
            <CheckBox Margin="0,12,0,0"
                      IsChecked="{Binding SubsAccumulationEnabled, UpdateSourceTrigger=PropertyChanged}">
                <TextBlock Text="{loc:Translate 'Accumulate subs across events'}"
                           TextWrapping="Wrap" />
            </CheckBox>
            <TextBlock Margin="26,4,0,0"
                       Foreground="{DynamicResource MutedBrush}"
                       TextWrapping="Wrap"
                       Text="{loc:Translate 'Subs, resubs, and gift subs add to a running count until the trigger is reached.'}" />
            <StackPanel Margin="26,8,0,0"
                        Visibility="{Binding SubsAccumulationEnabled, Converter={StaticResource BoolToVisibilityConverter}}">
                <CheckBox IsChecked="{Binding SubsCarryOverEnabled, UpdateSourceTrigger=PropertyChanged}">
                    <TextBlock Text="{loc:Translate 'Carry over excess subs'}"
                               TextWrapping="Wrap" />
                </CheckBox>
                <TextBlock Margin="26,4,0,0"
                           Foreground="{DynamicResource MutedBrush}"
                           TextWrapping="Wrap"
                           Text="{loc:Translate 'Extra subs past the threshold carry to the next round instead of resetting to zero.'}" />
            </StackPanel>
            <TextBlock Margin="0,12,0,0"
                       Foreground="{DynamicResource AccentBrush}"
                       TextWrapping="Wrap"
                       Text="{Binding SubsTriggerSummary}" />
        </StackPanel>
    </Border>
</StackPanel>
```

### Task 5: Update Inline Summary Display

**Files:**
- Modify: `VrcTwitchOscBridge\UserControls\InlineSubsRuleRowViewModel.cs`

**Interfaces:**
- Consumes: `TriggerRule.SubsTriggerCount`, `TriggerRule.SubsAccumulationEnabled`, `TriggerRule.SubsCarryOverEnabled`

- [ ] **Step 1: Update `RefreshSummary()` method (lines 43-73)**

Replace the middle part of the method. Add trigger count info after the name, before tier seconds:

```csharp
public void RefreshSummary()
{
    var name = string.IsNullOrWhiteSpace(_rule.Name) ? "Untitled" : _rule.Name;
    var sb = new StringBuilder();
    sb.Append("⭐ ").Append(name);

    // Trigger count info
    sb.Append(" — trigger: ").Append(Math.Max(1, _rule.SubsTriggerCount)).Append(" subs");

    var parts = new List<string>();
    if (_rule.SubscriptionTier1SecondsPerSub > 0) parts.Add($"T1:{_rule.SubscriptionTier1SecondsPerSub}s");
    if (_rule.SubscriptionTier2SecondsPerSub > 0) parts.Add($"T2:{_rule.SubscriptionTier2SecondsPerSub}s");
    if (_rule.SubscriptionTier3SecondsPerSub > 0) parts.Add($"T3:{_rule.SubscriptionTier3SecondsPerSub}s");
    if (parts.Count > 0) sb.Append(", ").Append(string.Join(" ", parts));

    // Accumulation / carryover
    if (_rule.SubsAccumulationEnabled)
    {
        sb.Append(", accumulate ON");
        if (_rule.SubsCarryOverEnabled)
        {
            sb.Append(", carryover ON");
        }
    }
    else
    {
        sb.Append(", accumulate OFF");
    }

    if (_rule.SubscriptionsAmountUnitsPerDuration > 0 && _rule.SubscriptionsSecondsPerAmountUnit > 0)
    {
        sb.Append(", ").Append(_rule.SubscriptionsSecondsPerAmountUnit)
          .Append("s per ").Append(_rule.SubscriptionsAmountUnitsPerDuration).Append(" subs");
    }
    if (_rule.MaxAccumulatedDurationEnabled && _rule.MaxAccumulatedDurationSeconds > 0)
    {
        sb.Append(", cap ").Append(_rule.MaxAccumulatedDurationSeconds).Append("s");
    }
    var subType = _rule.IsGiftSubscription ? "regular+gift" : "regular";
    sb.Append(", sub-type: ").Append(subType);
    if (!string.IsNullOrWhiteSpace(_rule.SupporterKeywordText))
    {
        sb.Append(", keyword: ").Append(_rule.SupporterKeywordText);
    }
    if (_rule.AddBitsToSwapTime)
    {
        sb.Append(", swap time");
    }
    Summary = sb.ToString();
}
```

### Task 6: Localization Keys

**Files:**
- Modify: `VrcTwitchOscBridge\Resources\Localization\en-US.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\en-US.extra.json`

- [ ] **Step 1: Add UI text keys to `en-US.json`**

Add at the end, before the closing `}`:

```json
  "Subscription Trigger Settings": "Subscription Trigger Settings",
  "Subs to Trigger": "Subs to Trigger",
  "How many subs are needed before this rule fires. Gift subs count by the total gifted.": "How many subs are needed before this rule fires. Gift subs count by the total gifted.",
  "Accumulate subs across events": "Accumulate subs across events",
  "Subs, resubs, and gift subs add to a running count until the trigger is reached.": "Subs, resubs, and gift subs add to a running count until the trigger is reached.",
  "Carry over excess subs": "Carry over excess subs",
  "Extra subs past the threshold carry to the next round instead of resetting to zero.": "Extra subs past the threshold carry to the next round instead of resetting to zero.",
  "Trigger: {0} subs": "Trigger: {0} subs",
  "accumulate ON": "accumulate ON",
  "accumulate OFF": "accumulate OFF",
  "carryover ON": "carryover ON",
  "Accumulate {0} subs": "Accumulate {0} subs",
  "Accumulate {0} subs (T1 {1}s, T2 {2}s, T3 {3}s)": "Accumulate {0} subs (T1 {1}s, T2 {2}s, T3 {3}s)"
}
```

Make sure there's a comma after the last existing entry when inserting.

- [ ] **Step 2: Add backup translations to `en-US.extra.json` (same set of keys)**

Add the same key-value pairs to `en-US.extra.json` at the end.

### Task 7: Build Verification

- [ ] **Step 1: Build the project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors.

- [ ] **Step 2: Run the test project**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: All tests pass.
