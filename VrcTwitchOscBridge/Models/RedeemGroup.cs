using System.Collections.ObjectModel;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class RedeemGroup : ObservableObject
{
    private string name = string.Empty;
    private string commandText = string.Empty;
    private ObservableCollection<Guid> assignedRuleIds = [];

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value?.Trim() ?? string.Empty);
    }

    public string CommandText
    {
        get => commandText;
        set => SetProperty(ref commandText, ChatCommandUtility.Normalize(value));
    }

    public ObservableCollection<Guid> AssignedRuleIds
    {
        get => assignedRuleIds;
        set => SetProperty(ref assignedRuleIds, value ?? []);
    }
}
