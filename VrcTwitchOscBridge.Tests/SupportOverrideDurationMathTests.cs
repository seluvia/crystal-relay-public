using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services.Support;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class SupportOverrideDurationMathTests
{
    [Fact]
    public void ComputePerEventAddSeconds_ToggleOff_ReturnsBaseDuration()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: false,
            durationSeconds: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(30, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BitsZeroBase_ReturnsScaledOnly()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 0,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(2, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BitsWithBase_ReturnsBasePlusScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 30,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(32, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BitsDifferentRatio_ReturnsBasePlusScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 30,
            bitsAmountUnitsPerDuration: 25,
            bitsSecondsPerAmountUnit: 2);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 50, subscriptionTier: string.Empty);

        Assert.Equal(34, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_SubsTier1_ReturnsBasePlusScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            triggerType: TwitchTriggerType.Subscriptions,
            amountScaledDurationEnabled: true,
            durationSeconds: 60,
            subscriptionTier1SecondsPerSub: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 1, subscriptionTier: "1000");

        Assert.Equal(90, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_GiftSubs_ReturnsBasePlusScaledTimesCount()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            triggerType: TwitchTriggerType.GiftSubscription,
            amountScaledDurationEnabled: true,
            durationSeconds: 60,
            subscriptionTier1SecondsPerSub: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 5, subscriptionTier: "1000");

        Assert.Equal(210, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_ZeroBase_ScalingOn_ReturnsScaledOnly()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 0,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 50, subscriptionTier: string.Empty);

        Assert.Equal(1, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BitsZeroRatio_FallsBackToAmountTimesSeconds()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 10,
            bitsAmountUnitsPerDuration: 0,
            bitsSecondsPerAmountUnit: 3);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 7, subscriptionTier: string.Empty);

        Assert.Equal(31, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_AddBitsToSwapTimeOff_ReturnsBaseDuration()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            addBitsToSwapTime: false,
            amountScaledDurationEnabled: false,
            durationSeconds: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(30, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_AddBitsToSwapTimeOn_ReturnsBasePlusScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            addBitsToSwapTime: true,
            amountScaledDurationEnabled: false,
            durationSeconds: 30,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(32, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_AddBitsToSwapTimeOn_SubsT1()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            triggerType: TwitchTriggerType.Subscriptions,
            addBitsToSwapTime: true,
            amountScaledDurationEnabled: false,
            durationSeconds: 60,
            subscriptionTier1SecondsPerSub: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 1, subscriptionTier: "1000");

        Assert.Equal(90, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BothTogglesOff_ReturnsBaseDuration()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            addBitsToSwapTime: false,
            amountScaledDurationEnabled: false,
            durationSeconds: 25);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(25, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BothTogglesOn_StillScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            addBitsToSwapTime: true,
            amountScaledDurationEnabled: true,
            durationSeconds: 30,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(32, result);
    }
}
