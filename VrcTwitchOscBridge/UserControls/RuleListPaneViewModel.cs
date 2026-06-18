using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.UserControls;

public sealed class RuleListPaneViewModel : ObservableObject
{
    public RuleListPaneViewModel(string? title = null)
    {
        Title = title;
    }

    public string? Title { get; }
}
