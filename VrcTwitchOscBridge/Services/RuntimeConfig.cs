using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Services;

public sealed class RuntimeConfig
{
    public const string DefaultTwitchClientId = "gm0ihiq9yqljizmnbixp3l0os065l7";
    public const string DefaultLiveFeedbackHeartbeatEndpoint = "https://crystal-relay-live-worker.screminpal-animation.workers.dev/api/ping";

    [JsonIgnore]
    public string TwitchClientId => DefaultTwitchClientId;

    public string SupplementalAboutProfilesEndpoint { get; set; } = string.Empty;

    public string SupplementalAboutProfilesHeaderName { get; set; } = string.Empty;

    public string SupplementalAboutProfilesHeaderValue { get; set; } = string.Empty;

    [JsonPropertyName("liveFeedbackHeartbeatEndpoint")]
    public string LiveFeedbackHeartbeatEndpoint { get; set; } = DefaultLiveFeedbackHeartbeatEndpoint;

    public RuntimeConfig Normalize()
    {
        SupplementalAboutProfilesEndpoint = SupplementalAboutProfilesEndpoint.Trim();
        SupplementalAboutProfilesHeaderName = SupplementalAboutProfilesHeaderName.Trim();
        SupplementalAboutProfilesHeaderValue = SupplementalAboutProfilesHeaderValue.Trim();
        LiveFeedbackHeartbeatEndpoint = string.IsNullOrWhiteSpace(LiveFeedbackHeartbeatEndpoint)
            ? DefaultLiveFeedbackHeartbeatEndpoint
            : LiveFeedbackHeartbeatEndpoint.Trim();
        return this;
    }

    public static RuntimeConfig CreateDefault() => new RuntimeConfig().Normalize();
}
