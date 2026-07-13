using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarScaleSafetySettingsTests
{
    [Fact]
    public void Defaults_UseSafeAvatarScaleRange()
    {
        var settings = new AvatarScaleSafetySettings();

        Assert.Equal(AvatarScaleRule.SafeMinimumHeightMeters, settings.CurrentMinimumHeightMeters);
        Assert.Equal(AvatarScaleRule.SafeMaximumHeightMeters, settings.CurrentMaximumHeightMeters);
        Assert.Equal("100m", settings.CurrentMaxHeightAllowedText);
    }

    [Theory]
    [InlineData(double.NaN, 1.6)]
    [InlineData(double.PositiveInfinity, 1.6)]
    [InlineData(0.01, 0.1)]
    [InlineData(2.4, 2.4)]
    [InlineData(500, 100)]
    public void ClampHeight_UsesCurrentRange(double value, double expected)
    {
        var settings = new AvatarScaleSafetySettings();

        Assert.Equal(expected, settings.ClampHeight(value), precision: 3);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ClampHeight_InvalidValuesRespectCurrentMinimum(double value)
    {
        var settings = new AvatarScaleSafetySettings
        {
            CurrentMinimumHeightMeters = 5,
            CurrentMaximumHeightMeters = 10
        };

        Assert.Equal(5, settings.ClampHeight(value), precision: 3);
    }

    [Fact]
    public void CurrentMaximumHeightMeters_ClampsToAdvancedRangeAndKeepsMinimumBelowMaximum()
    {
        var settings = new AvatarScaleSafetySettings
        {
            CurrentMinimumHeightMeters = 5,
            CurrentMaximumHeightMeters = 2
        };

        Assert.Equal(5, settings.CurrentMinimumHeightMeters);
        Assert.Equal(5, settings.CurrentMaximumHeightMeters);

        settings.CurrentMaximumHeightMeters = 20000;

        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, settings.CurrentMaximumHeightMeters);
    }

    [Fact]
    public void FromExistingRules_UsesLargestAdvancedValueAboveSafeDefault()
    {
        var rules = new[]
        {
            new AvatarScaleRule
            {
                AdvancedRangeEnabled = true,
                TargetHeightMeters = 250,
                MaximumHeightMeters = 150,
                RestoreHeightMeters = 1.6
            },
            new AvatarScaleRule
            {
                AdvancedRangeEnabled = false,
                TargetHeightMeters = 500
            }
        };

        var settings = AvatarScaleSafetySettings.FromExistingRules(rules);

        Assert.Equal(250, settings.CurrentMaximumHeightMeters);
    }

    [Fact]
    public void FromExistingRules_DefaultsToSafeMaxWhenNoAdvancedValuesExist()
    {
        var settings = AvatarScaleSafetySettings.FromExistingRules(new List<AvatarScaleRule>
        {
            new() { TargetHeightMeters = 2.4, MaximumHeightMeters = 3 }
        });

        Assert.Equal(AvatarScaleRule.SafeMaximumHeightMeters, settings.CurrentMaximumHeightMeters);
    }

    [Theory]
    [InlineData(nameof(AvatarScaleRule.TargetHeightMeters), 0.05)]
    [InlineData(nameof(AvatarScaleRule.MinimumHeightMeters), 0.02)]
    public void FromExistingRules_UsesSmallestAdvancedValueBelowSafeDefaultForMinimum(string propertyName, double configuredHeight)
    {
        var rule = new AvatarScaleRule
        {
            AdvancedRangeEnabled = true
        };
        switch (propertyName)
        {
            case nameof(AvatarScaleRule.TargetHeightMeters):
                rule.TargetHeightMeters = configuredHeight;
                break;
            case nameof(AvatarScaleRule.MinimumHeightMeters):
                rule.MinimumHeightMeters = configuredHeight;
                break;
        }

        var settings = AvatarScaleSafetySettings.FromExistingRules(new[] { rule });

        Assert.Equal(configuredHeight, settings.CurrentMinimumHeightMeters, precision: 3);
    }

    [Fact]
    public void FromExistingRules_IncludesRelativeHeightMetersAboveSafeDefault()
    {
        var settings = AvatarScaleSafetySettings.FromExistingRules(new[]
        {
            new AvatarScaleRule
            {
                AdvancedRangeEnabled = true,
                RelativeHeightMeters = 180
            }
        });

        Assert.Equal(180, settings.CurrentMaximumHeightMeters);
    }

    [Fact]
    public void FromExistingRules_IncludesNestedCashAndPowerUpScaleActionsWhenCallerAggregatesThem()
    {
        var cash = new CashPaymentRule { ActionKind = CashPaymentActionKind.AvatarScaling };
        cash.ScaleAction.AdvancedRangeEnabled = true;
        cash.ScaleAction.MaximumHeightMeters = 180;

        var power = new PowerUpRule { ActionKind = PowerUpActionKind.AvatarScaling };
        power.ScaleAction.AdvancedRangeEnabled = true;
        power.ScaleAction.TargetHeightMeters = 240;

        var allRules = new[]
        {
            cash.ScaleAction,
            power.ScaleAction
        };

        var settings = AvatarScaleSafetySettings.FromExistingRules(allRules);

        Assert.Equal(240, settings.CurrentMaximumHeightMeters);
    }

    [Fact]
    public void SettingsStorePersistedProfile_RoundTripsAvatarScaleSafetyThroughDtoSerialization()
    {
        var source = new AvatarScaleSafetySettings
        {
            CurrentMinimumHeightMeters = 0.25,
            CurrentMaximumHeightMeters = 240
        };
        var storeType = typeof(SettingsStore);
        var toPersistedMethod = storeType.GetMethod("ToPersistedAvatarScaleSafety", BindingFlags.NonPublic | BindingFlags.Static);
        var toSettingsMethod = storeType.GetMethod("ToAvatarScaleSafety", BindingFlags.NonPublic | BindingFlags.Static);
        var profileType = storeType.GetNestedType("PersistedProfileSettings", BindingFlags.NonPublic);

        Assert.NotNull(toPersistedMethod);
        Assert.NotNull(toSettingsMethod);
        Assert.NotNull(profileType);
        var avatarScaleSafetyProperty = profileType.GetProperty("AvatarScaleSafety", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(avatarScaleSafetyProperty);

        var persistedSafety = toPersistedMethod.Invoke(null, [source]);
        Assert.NotNull(persistedSafety);
        var profile = Activator.CreateInstance(profileType, nonPublic: true);
        Assert.NotNull(profile);
        avatarScaleSafetyProperty.SetValue(profile, persistedSafety);

        var json = JsonSerializer.Serialize(profile, profileType);
        Assert.Contains("AvatarScaleSafety", json, StringComparison.Ordinal);
        Assert.Contains("CurrentMinimumHeightMeters", json, StringComparison.Ordinal);
        Assert.Contains("CurrentMaximumHeightMeters", json, StringComparison.Ordinal);

        var rehydratedProfile = JsonSerializer.Deserialize(json, profileType);
        Assert.NotNull(rehydratedProfile);
        var rehydratedSafetyDto = avatarScaleSafetyProperty.GetValue(rehydratedProfile);
        Assert.NotNull(rehydratedSafetyDto);

        var roundTripped = Assert.IsType<AvatarScaleSafetySettings>(toSettingsMethod.Invoke(
            null,
            [rehydratedSafetyDto, Array.Empty<AvatarScaleRule>()]));

        Assert.Equal(0.25, roundTripped.CurrentMinimumHeightMeters, precision: 3);
        Assert.Equal(240, roundTripped.CurrentMaximumHeightMeters, precision: 3);
    }
}
