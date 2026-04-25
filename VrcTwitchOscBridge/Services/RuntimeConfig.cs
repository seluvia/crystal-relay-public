using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Services;

public sealed class RuntimeConfig
{
    public const string DefaultTwitchClientId = "gm0ihiq9yqljizmnbixp3l0os065l7";

    [JsonIgnore]
    public string TwitchClientId => DefaultTwitchClientId;

    public RuntimeConfig Normalize() => this;

    public static RuntimeConfig CreateDefault() => new RuntimeConfig().Normalize();
}
