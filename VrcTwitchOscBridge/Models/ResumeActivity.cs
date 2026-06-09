using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Models;

public sealed class ResumeActivity
{
    [JsonPropertyName("type")]
    public ResumeActivityType Type { get; set; }

    [JsonPropertyName("ruleId")]
    public Guid RuleId { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("currentValue")]
    public double? CurrentValue { get; set; }

    [JsonPropertyName("payload")]
    public Dictionary<string, object> Payload { get; set; } = new();
}