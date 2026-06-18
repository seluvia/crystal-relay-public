using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed class CashPaymentRuleJsonConverter : JsonConverter<CashPaymentRule>
{
    public override CashPaymentRule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var clonedOptions = new JsonSerializerOptions(options);
        clonedOptions.Converters.Remove(this);

        var hasActionType = root.TryGetProperty("ActionType", out _);
        var hasProvider = root.TryGetProperty("Provider", out _);

        if (hasActionType && !hasProvider)
        {
            var legacy = JsonSerializer.Deserialize<TriggerRule>(root.GetRawText(), clonedOptions);
            if (legacy is null) return new CashPaymentRule();
            System.Diagnostics.Debug.WriteLine(
                $"Avatar Swap migration: dropped legacy payment rule fields for {legacy.Name}");
            return new CashPaymentRule
            {
                Name = legacy.Name,
                Provider = CashPaymentProvider.StreamElements,
                MinimumAmount = (decimal)legacy.MinimumAmount,
                IsEnabled = true,
                ActionKind = CashPaymentActionKind.TriggerAction,
                TriggerAction = new TriggerRule
                {
                    ActionType = legacy.ActionType,
                    AvatarChangeTargetId = legacy.AvatarChangeTargetId,
                    AvatarTargetName = legacy.AvatarTargetName
                }
            };
        }

        return JsonSerializer.Deserialize<CashPaymentRule>(root.GetRawText(), clonedOptions)
            ?? new CashPaymentRule();
    }

    public override void Write(Utf8JsonWriter writer, CashPaymentRule value, JsonSerializerOptions options)
    {
        var clonedOptions = new JsonSerializerOptions(options);
        clonedOptions.Converters.Remove(this);
        JsonSerializer.Serialize(writer, value, clonedOptions);
    }
}
