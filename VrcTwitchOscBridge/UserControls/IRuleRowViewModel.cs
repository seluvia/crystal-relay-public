using System.Windows.Input;

namespace VrcTwitchOscBridge.UserControls;

public interface IRuleRowViewModel
{
    object Rule { get; }
    string Summary { get; }
    bool IsEnabled { get; }
    ICommand EditCommand { get; set; }
    ICommand DeleteCommand { get; set; }
    void RefreshSummary();
}
