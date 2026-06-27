using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.UserControls;

public enum RuleListPaneKind
{
    Swap,
    Roulette
}

public sealed class RuleListPaneViewModel : ObservableObject
{
    public RuleListPaneViewModel(RuleListPaneKind kind, string? title = null)
    {
        Kind = kind;
        Title = title;
    }

    public RuleListPaneKind Kind { get; }

    public string? Title { get; }
}
