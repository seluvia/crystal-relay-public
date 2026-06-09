using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Models;

public sealed class ActivityResumeSnapshot
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("savedAt")]
    public DateTimeOffset SavedAt { get; set; }

    [JsonPropertyName("currentAvatarId")]
    public string CurrentAvatarId { get; set; } = string.Empty;

    [JsonPropertyName("activities")]
    public List<ResumeActivity> Activities { get; set; } = new();
}