using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BridgeRuntimeConfigurationAvatarScaleSafetyTests
{
    [Fact]
    public void FromSettings_ClampsNormalScaleRuleToCurrentMaxHeightAllowed()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = 2;
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Too Tall",
            TriggerType = AvatarScaleTriggerType.ChatCommand,
            CommandText = "!tall",
            AdvancedRangeEnabled = true,
            TargetHeightMeters = 50,
            RestoreHeightMeters = 20
        });
        settings.AvatarScaleSets.Add(set);

        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var snapshot = Assert.Single(configuration.AvatarScaleRules);

        Assert.Equal(2, snapshot.TargetHeightMeters, precision: 3);
        Assert.Equal(2, snapshot.RestoreHeightMeters, precision: 3);
        Assert.Equal(2, snapshot.CurrentMaximumHeightAllowedMeters, precision: 3);
    }

    [Fact]
    public void FromSettings_PreservesAdvancedScaleRuleMaximumAtTenThousandMeters()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Maximum Advanced Height",
            TriggerType = AvatarScaleTriggerType.ChatCommand,
            CommandText = "!maximum",
            AdvancedRangeEnabled = true,
            ScaleMode = AvatarScaleMode.RelativeHeight,
            TargetHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters,
            MinimumHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters,
            MaximumHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters,
            RestoreHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters,
            RelativeHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters,
            RelativeMinimumHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters,
            RelativeMaximumHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters
        });
        settings.AvatarScaleSets.Add(set);

        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var snapshot = Assert.Single(configuration.AvatarScaleRules);

        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, snapshot.TargetHeightMeters, precision: 3);
        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, snapshot.MinimumHeightMeters, precision: 3);
        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, snapshot.MaximumHeightMeters, precision: 3);
        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, snapshot.RestoreHeightMeters, precision: 3);
        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, snapshot.RelativeHeightMeters, precision: 3);
        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, snapshot.RelativeMinimumHeightMeters, precision: 3);
        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, snapshot.RelativeMaximumHeightMeters, precision: 3);
        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, snapshot.CurrentMaximumHeightAllowedMeters, precision: 3);
    }

    [Fact]
    public void FromSettings_RespectsExplicitOneHundredMeterSafetyMaximumForAdvancedRule()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = AvatarScaleRule.SafeMaximumHeightMeters;
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Explicit Safe Maximum",
            TriggerType = AvatarScaleTriggerType.ChatCommand,
            CommandText = "!safe",
            AdvancedRangeEnabled = true,
            TargetHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters,
            MinimumHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters,
            MaximumHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters,
            RestoreHeightMeters = AvatarScaleRule.AdvancedMaximumHeightMeters
        });
        settings.AvatarScaleSets.Add(set);

        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var snapshot = Assert.Single(configuration.AvatarScaleRules);

        Assert.Equal(AvatarScaleRule.SafeMaximumHeightMeters, snapshot.TargetHeightMeters, precision: 3);
        Assert.Equal(AvatarScaleRule.SafeMaximumHeightMeters, snapshot.CurrentMaximumHeightAllowedMeters, precision: 3);
    }

    [Fact]
    public void FromSettings_ClampsCashPaymentScaleActionToCurrentMaxHeightAllowed()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = 3;
        var rule = new CashPaymentRule
        {
            Name = "Tip Tall",
            ActionKind = CashPaymentActionKind.AvatarScaling,
            Provider = CashPaymentProvider.StreamElements,
            MinimumAmount = 1
        };
        rule.ScaleAction.AdvancedRangeEnabled = true;
        rule.ScaleAction.TargetHeightMeters = 50;
        settings.CashPaymentRules.Add(rule);

        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var snapshot = Assert.Single(configuration.CashPaymentRules);

        Assert.NotNull(snapshot.ScaleAction);
        Assert.Equal(3, snapshot.ScaleAction!.TargetHeightMeters, precision: 3);
        Assert.Equal(3, snapshot.ScaleAction.CurrentMaximumHeightAllowedMeters, precision: 3);
    }

    [Fact]
    public void FromSettings_KeepsNonAdvancedScaleRuleWithinSafeMaxWhenSafetyRangeIsAboveSafeMax()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMinimumHeightMeters = AvatarScaleRule.SafeMaximumHeightMeters + 50;
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = AvatarScaleRule.SafeMaximumHeightMeters + 100;
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Normal Safe Max",
            TriggerType = AvatarScaleTriggerType.ChatCommand,
            CommandText = "!normalmax",
            AdvancedRangeEnabled = false,
            TargetHeightMeters = AvatarScaleRule.SafeMaximumHeightMeters
        });
        settings.AvatarScaleSets.Add(set);

        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var snapshot = Assert.Single(configuration.AvatarScaleRules);

        Assert.False(snapshot.AdvancedRangeEnabled);
        Assert.Equal(AvatarScaleRule.SafeMaximumHeightMeters, snapshot.TargetHeightMeters, precision: 3);
        Assert.Equal(AvatarScaleRule.SafeMaximumHeightMeters, snapshot.CurrentMaximumHeightAllowedMeters, precision: 3);
    }

    [Fact]
    public void FromSettings_ClampsSupporterGrowthTierHeightAddsToCurrentMaxHeightAllowed()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = 2;
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Growth Tiers",
            TriggerType = AvatarScaleTriggerType.SupporterGrowth,
            AdvancedRangeEnabled = true,
            SupporterGrowthTier1HeightMeters = 20,
            SupporterGrowthTier2HeightMeters = 30,
            SupporterGrowthTier3HeightMeters = 40
        });
        settings.AvatarScaleSets.Add(set);

        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var snapshot = Assert.Single(configuration.AvatarScaleRules);

        Assert.Equal(2, snapshot.SupporterGrowthTier1HeightMeters, precision: 3);
        Assert.Equal(2, snapshot.SupporterGrowthTier2HeightMeters, precision: 3);
        Assert.Equal(2, snapshot.SupporterGrowthTier3HeightMeters, precision: 3);
    }

    [Fact]
    public void FromSettings_ClampsSupporterGrowthBitRangeHeightAddsToCurrentMaxHeightAllowed()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = 4;
        var set = new AvatarScaleSet();
        var rule = new AvatarScaleRule
        {
            Name = "Growth Bits",
            TriggerType = AvatarScaleTriggerType.SupporterGrowth,
            AdvancedRangeEnabled = true
        };
        rule.SupporterGrowthBitRanges.Clear();
        rule.SupporterGrowthBitRanges.Add(new AvatarScaleBitGrowthRange
        {
            MinimumBits = 1,
            MaximumBits = 100,
            HeightAddedMeters = 50
        });
        set.ScaleRules.Add(rule);
        settings.AvatarScaleSets.Add(set);

        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var snapshot = Assert.Single(configuration.AvatarScaleRules);
        var range = Assert.Single(snapshot.SupporterGrowthBitRanges);

        Assert.Equal(4, range.HeightAddedMeters, precision: 3);
    }

    [Fact]
    public void CreateManualTestSnapshot_UsesProvidedSafetyBelowDefaultCurrentMaxHeightAllowed()
    {
        var safety = new AvatarScaleSafetySettings
        {
            CurrentMaximumHeightMeters = 2
        };
        var rule = new AvatarScaleRule
        {
            Name = "Manual Tall",
            AdvancedRangeEnabled = true,
            TargetHeightMeters = 50,
            RestoreHeightMeters = 20
        };

        var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, safety);

        Assert.Equal(2, snapshot.TargetHeightMeters, precision: 3);
        Assert.Equal(2, snapshot.RestoreHeightMeters, precision: 3);
        Assert.Equal(2, snapshot.CurrentMaximumHeightAllowedMeters, precision: 3);
    }
}
