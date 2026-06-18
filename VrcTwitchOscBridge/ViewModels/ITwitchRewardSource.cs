using System.Collections.ObjectModel;
using System.Windows.Input;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge.ViewModels;

public interface ITwitchRewardSource
{
    ObservableCollection<TwitchRewardOption> RewardOptions { get; }
    ICommand RefreshTwitchRewardsCommand { get; }
    ICommand UnlinkTwitchRewardCommand { get; }
}
