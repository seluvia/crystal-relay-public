# Avatar Swap Single Name Field Design

## Objective

Simplify the Avatar Swap channel point reward creation by merging the internal trigger name and Twitch reward name into a single "Name" field.

## Scope

- Avatar Swap window only
- Channel point rewards only
- Bits, Subs, Payment, and Chat Command triggers unaffected

## Changes

### 1. `InlineRuleEditorControl.xaml` — Remove Reward Name textbox

Remove lines 122-125 (the "Reward Name" `TextBlock` + `TextBox` pair from the Channel Points section). The "Reward Cost" field stays but now sits alone in its `UniformGrid` column (or the UniformGrid can be removed and the cost field placed directly).

### 2. `TriggerRule.cs` — Sync Name to ChannelPointRewardTitle

In the `Name` setter, when `TriggerType == TwitchTriggerType.ChannelPoints`, also update `ChannelPointRewardTitle` to the same value. This keeps them in sync whenever the user types in the Name field.

```csharp
public string Name
{
    get => name;
    set
    {
        if (SetProperty(ref name, value))
        {
            if (TriggerType == TwitchTriggerType.ChannelPoints)
            {
                if (!string.Equals(channelPointRewardTitle, value, StringComparison.Ordinal))
                {
                    channelPointRewardTitle = value;
                    OnPropertyChanged(nameof(ChannelPointRewardTitle));
                }
            }
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }
}
```

### 3. `AvatarSwapManagerViewModel.cs` — Set ChannelPointRewardTitle on create

In `AddChannelPointRule()`, set `ChannelPointRewardTitle = Name` so new rules start synced.

### Migration

Existing channel point rules where `Name` and `ChannelPointRewardTitle` differ will NOT be migrated. Once the user edits the "Name" field, it will overwrite `ChannelPointRewardTitle`. This is intentional — the two values merging is the desired behavior going forward.

## Files to Modify

1. `VrcTwitchOscBridge\UserControls\InlineRuleEditorControl.xaml` — remove Reward Name textbox
2. `VrcTwitchOscBridge\Models\TriggerRule.cs` — sync Name → ChannelPointRewardTitle for channel points
3. `VrcTwitchOscBridge\ViewModels\AvatarSwapManagerViewModel.cs` — set ChannelPointRewardTitle on create

## Not Changed

- Other trigger types (bits, subs, payment, chat command)
- Other windows (Avatar Scaling, Universal Triggers, Avatar Sets)
- Data model serialization (ChannelPointRewardTitle stays in the model)
