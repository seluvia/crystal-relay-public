namespace VrcTwitchOscBridge.Models;

public sealed record VrChatOscParameterSummary(string Address, string Name, OscParameterType ParameterType)
{
    public string DisplayLabel => $"{Name} [{ParameterType}]";
}
