# Avatar Scaling Manager — Supporter Growth Fields & Extend Checkbox Scope

**Date:** 2026-07-03
**Scope:** `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` only — the Supporter Growth editor block inside the "Timer & Return" border, and the "Extend the current active activity" border that follows it. No other windows, no model changes, no runtime behavior changes.

## Problem

The new Avatar Scaling Manager window's Supporter Growth editor (`AvatarScalingManagerWindow.xaml:1573-1607`) is missing most of the configuration fields that the legacy `MainWindow.xaml` Supporter Growth panel exposes. A streamer can only set Normal Height, Max Added Height, and Bits Growth Ranges — they cannot configure sub tier heights/seconds, bits timer unit, seconds-per-bits-unit, soft cap, max paid time, grow/shrink cheer keywords, or the reward-scale overlay toggle.

Separately, the "Extend the current active activity instead of running this rule's action" checkbox (`AvatarScalingManagerWindow.xaml:1611-1630`) is always visible for every trigger type, including Supporter Growth. The user wants that checkbox to be a Twitch channel-point-reward / chat-command / bits / subs / follow concern, not a Supporter Growth concern — Supporter Growth feeds its own timer and should not extend some other active activity.

## Goals

1. Bring the new Avatar Scaling Manager's Supporter Growth editor to parity with the legacy `MainWindow.xaml` Supporter Growth panel — every field the model already exposes should be editable.
2. Hide the "Extend the current active activity" checkbox + "Extend by (seconds)" input when the selected rule's trigger type is `SupporterGrowth`. Show it for every other trigger type (Channel Point Reward, Chat Command, Bits, Subscription, Gift Subscription, Follow).

## Non-Goals

- No changes to `AvatarScaleRule` or any other model.
- No changes to `BridgeCoordinator` runtime logic. `ExtendCurrentActivity` already works as-is for the trigger types where it stays visible; for Supporter Growth it is simply not exposed in the new UI. Existing saved values on Supporter Growth rules are left as-is in storage and ignored by the UI (runtime already treats Supporter Growth as its own path).
- No new localization keys. Every label and helper text below already exists in `en-US.extra.json` and the matching `.extra.json` files because the legacy `MainWindow.xaml` uses them.
- No changes to the legacy `MainWindow.xaml` Supporter Growth panel. It remains as the fallback for the old UI.

## Design

### Change 1 — Expand the Supporter Growth section

Location: `AvatarScalingManagerWindow.xaml`, inside the "Timer & Return" border, the existing `<StackPanel Margin="0,12,0,0" Visibility="{Binding UsesSupporterGrowth, ...}">` block at line 1573.

The block is reorganized into labeled sub-groups that mirror the legacy `MainWindow.xaml` Supporter Growth panel ordering. From top to bottom inside the `UsesSupporterGrowth` StackPanel:

1. **Section header** — existing `<TextBlock Text="{loc:Translate 'Supporter Growth'}" FontWeight="SemiBold" />`.
2. **Description** — `<TextBlock Text="{loc:Translate 'Supporter Growth listens to bits, new subs, resubs, and gift subs. Paid events add height and add fair active time, then return to normal when the paid timer ends.'}" />` with `TextWrapping="Wrap"` and the muted sub-text brush.
3. **Allow overlay checkbox** — `<CheckBox Content="{loc:Translate 'Allow reward scale changes during paid growth'}" IsChecked="{Binding SupporterGrowthAllowRewardScaleOverlay, UpdateSourceTrigger=PropertyChanged}" />` followed by the helper `<TextBlock Text="{loc:Translate 'When enabled, channel-point and chat scale redeems can temporarily adjust height during paid growth without changing the paid timer.'}" />` (muted, wrapping, indented `Margin="26,6,0,0"`).
4. **Normal Height | Max Added Height** — keep the existing 2-column `UniformGrid`. Add the "Use 0 for unlimited added height until VRChat or safe range clamps it." helper under the Max Added Height column.
5. **Paid Active Time** subheader (`<TextBlock FontWeight="SemiBold" />`) + helper "Paid time is shared by bits, subs, resubs, and gift subs. Time adds to the remaining paid timer, then slows above the soft cap and never exceeds the max." Then a 3-column `UniformGrid` row with **Bits Timer Unit**, **Seconds Per Bits Unit**, **Soft Cap Seconds**, and a second 3-column row with **Soft Cap Multiplier Percent**, **Max Paid Time Seconds**, and an empty cell (or a short helper). The `Smooth Transition Seconds` field is NOT duplicated here — it already lives in the Timer & Return row above and stays there.
6. **Supporter Growth Cheer Keywords** subheader + helper "Use cheer text like Cheer100 grow or Cheer100 shrink. No keyword keeps the existing positive growth behavior; if both words appear, Crystal Relay skips the scale instead of guessing." Then a 2-column `UniformGrid` with **Grow Keyword** | **Shrink Keyword**.
7. **Subscription Growth** subheader. 3-column `UniformGrid` with **Tier 1 Height Add** | **Tier 2 Height Add** | **Tier 3 Height Add**.
8. **Subscription Paid Time** subheader. 3-column `UniformGrid` with **Tier 1 Seconds** | **Tier 2 Seconds** | **Tier 3 Seconds**.
9. **Bits Growth Ranges** — keep the existing `DockPanel` header with the "Add Bits Range" button and the existing `ItemsControl` of range rows.
10. **"Maximum Bits set to 0 means no upper limit for that row."** helper under the ranges list (muted, wrapping).

All bindings target existing properties on `AvatarScaleRule`:
- `SupporterGrowthAllowRewardScaleOverlay`
- `SupporterGrowthNormalHeightMeters`, `SupporterGrowthMaxAddedHeightMeters`
- `SupporterGrowthBitsTimerUnit`, `SupporterGrowthSecondsPerBitsUnit`
- `SupporterGrowthSoftCapSeconds`, `SupporterGrowthSoftCapMultiplierPercent`, `SupporterGrowthMaxPaidTimeSeconds`
- `SupporterGrowthGrowKeyword`, `SupporterGrowthShrinkKeyword`
- `SupporterGrowthTier1HeightMeters`, `SupporterGrowthTier2HeightMeters`, `SupporterGrowthTier3HeightMeters`
- `SupporterGrowthTier1Seconds`, `SupporterGrowthTier2Seconds`, `SupporterGrowthTier3Seconds`
- `SupporterGrowthBitRanges` (existing `ItemsControl`)

All `UpdateSourceTrigger` values match the legacy panel: `PropertyChanged` for timer/cap/keyword/tier-seconds fields, `LostFocus` for height meters fields.

Styling follows the surrounding new UI: `FontWeight="SemiBold"` on labels, `Foreground="{DynamicResource TitleBarSubTextBrush}"` on helper text, `TextWrapping="Wrap"` on long helpers, `Margin` spacing consistent with the existing "Timer & Return" rows.

### Change 2 — Hide "Extend the current active activity" for Supporter Growth

Location: `AvatarScalingManagerWindow.xaml:1611-1630`, the `<Border>` that wraps the Extend checkbox and "Extend by (seconds)" input.

Add a `Border.Style` with a `Style.TargetType="Border"` that defaults `Visibility` to `Visible` and has a `DataTrigger` binding `{Binding UsesSupporterGrowth}` with `Value="True"` that sets `Visibility="Collapsed"`.

```xml
<Border Background="{DynamicResource NestedPanelBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1"
        CornerRadius="10"
        Padding="10"
        Margin="0,8,0,0">
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding UsesSupporterGrowth}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <StackPanel>
        <!-- existing ExtendCurrentActivity checkbox + ExtendSeconds TextBox -->
    </StackPanel>
</Border>
```

The checkbox and "Extend by (seconds)" input inside the border are unchanged. The border simply collapses when the rule is a Supporter Growth rule, so the checkbox and input take no space and cannot be edited for that trigger type.

The runtime behavior in `BridgeCoordinator.ExecuteAvatarScaleRuleAsync` already checks `rule.ExtendCurrentActivity && rule.ExtendSeconds > 0` before the Supporter Growth branch. Hiding the UI for Supporter Growth does not change that — it just prevents new edits. Any pre-existing `ExtendCurrentActivity=true` value on a Supporter Growth rule would still take effect at runtime, which matches the legacy UI behavior (the legacy `MainWindow.xaml` Extend checkbox was also unconditional). This is acceptable and out of scope for this change.

## Files Touched

- `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` — only this file.

## Localization

No new keys. Every label and helper text used in the expanded Supporter Growth block already exists in `Resources\Localization\en-US.extra.json` and every matching `.extra.json` file because the legacy `MainWindow.xaml` Supporter Growth panel uses them. The Extend checkbox label and "Extend by (seconds)" label already exist too.

Run the localization audit after the XAML edit to confirm no gaps were introduced by typo or rewording. Expected: audit passes with no new missing keys.

## Verification

1. `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore` — build succeeds.
2. Run the localization audit project — no new missing keys, no placeholder breakage.
3. Launch the debug build via `Launch-Crystal-Relay-Debug.bat`. Open the Avatar Scaling Manager. Add a Supporter Growth card and select it:
   - Confirm the Supporter Growth section shows all fields listed in Change 1.
   - Confirm changing each field persists after closing and reopening the manager.
   - Confirm the "Extend the current active activity" checkbox is NOT visible on the Supporter Growth card.
4. Add or select a Twitch Reward (Channel Point Reward) scale card:
   - Confirm the "Extend the current active activity" checkbox IS visible.
   - Confirm toggling it shows/hides the "Extend by (seconds)" input.
5. Switch a Twitch Reward card's trigger type through Chat Command, Bits, Subscription, Gift Subscription, Follow — confirm the Extend checkbox stays visible for each of those. Only Supporter Growth hides it.
