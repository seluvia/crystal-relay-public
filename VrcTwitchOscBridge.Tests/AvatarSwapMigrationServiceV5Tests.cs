using System.Text.Json;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceV5Tests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new CashPaymentRuleJsonConverter() }
    };

    [Fact]
    public void CurrentMigrationVersion_IsAtLeast5()
    {
        Assert.True(AvatarSwapMigrationService.CurrentMigrationVersion >= 5);
    }

    [Fact]
    public void Converter_ConvertsLegacyTriggerRuleJsonToCashPaymentRule()
    {
        // Enums serialize as integers in production (default System.Text.Json).
        // OscActionType.AvatarChange = 0, TwitchTriggerType.ChannelPoints = 0, TriggerRuleSource.CashPayment = 5.
        const string legacyJson = @"{
            ""Name"": ""Legacy Pay Rule"",
            ""ActionType"": 1,
            ""AvatarChangeTargetId"": ""avtr_target"",
            ""AvatarTargetName"": ""Target Avatar"",
            ""MinimumAmount"": 100,
            ""Source"": 5,
            ""ChannelPointRewardTitle"": ""dropped"",
            ""ChannelPointRewardCost"": 9999
        }";

        var rule = JsonSerializer.Deserialize<CashPaymentRule>(legacyJson, SerializerOptions);

        Assert.NotNull(rule);
        Assert.Equal("Legacy Pay Rule", rule!.Name);
        Assert.Equal(CashPaymentProvider.StreamElements, rule.Provider);
        Assert.Equal(100m, rule.MinimumAmount);
        Assert.True(rule.IsEnabled);
        Assert.Equal(CashPaymentActionKind.TriggerAction, rule.ActionKind);
        Assert.NotNull(rule.TriggerAction);
        Assert.Equal(OscActionType.AvatarChange, rule.TriggerAction!.ActionType);
        Assert.Equal("avtr_target", rule.TriggerAction.AvatarChangeTargetId);
        Assert.Equal("Target Avatar", rule.TriggerAction.AvatarTargetName);
    }

    [Fact]
    public void Converter_PassesThroughModernCashPaymentRule()
    {
        const string modernJson = @"{
            ""Name"": ""Modern"",
            ""Provider"": 2,
            ""MinimumAmount"": 5,
            ""MaximumAmount"": 50,
            ""CurrencyCode"": ""USD"",
            ""MessageContains"": ""cheer"",
            ""IsEnabled"": true,
            ""ActionKind"": 0
        }";

        var rule = JsonSerializer.Deserialize<CashPaymentRule>(modernJson, SerializerOptions);

        Assert.NotNull(rule);
        Assert.Equal("Modern", rule!.Name);
        Assert.Equal(CashPaymentProvider.KoFi, rule.Provider);
        Assert.Equal(5m, rule.MinimumAmount);
        Assert.Equal(50m, rule.MaximumAmount);
        Assert.Equal("USD", rule.CurrencyCode);
        Assert.Equal("cheer", rule.MessageContains);
        Assert.True(rule.IsEnabled);
        Assert.Equal(CashPaymentActionKind.TriggerAction, rule.ActionKind);
    }
}
