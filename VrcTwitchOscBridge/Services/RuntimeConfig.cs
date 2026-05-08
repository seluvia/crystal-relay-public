using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Services;

public sealed class RuntimeConfig
{
    public const string DefaultTwitchClientId = "gm0ihiq9yqljizmnbixp3l0os065l7";

    [JsonIgnore]
    public string TwitchClientId => DefaultTwitchClientId;

    public string SupplementalAboutProfilesEndpoint { get; set; } = string.Empty;

    public string SupplementalAboutProfilesHeaderName { get; set; } = string.Empty;

    public string SupplementalAboutProfilesHeaderValue { get; set; } = string.Empty;

    public RuntimeConfig Normalize()
    {
        SupplementalAboutProfilesEndpoint = SupplementalAboutProfilesEndpoint.Trim();
        SupplementalAboutProfilesHeaderName = SupplementalAboutProfilesHeaderName.Trim();
        SupplementalAboutProfilesHeaderValue = SupplementalAboutProfilesHeaderValue.Trim();
        return this;
    }

    public static RuntimeConfig CreateDefault() => new RuntimeConfig().Normalize();
}
