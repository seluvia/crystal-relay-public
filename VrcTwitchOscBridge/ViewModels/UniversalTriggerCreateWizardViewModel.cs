using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class UniversalTriggerCreateWizardViewModel : ObservableObject
{
    private int currentStep = 1;
    public int CurrentStep { get => currentStep; set => SetProperty(ref currentStep, Math.Clamp(value, 1, 4)); }

    private UniversalTriggerType selectedEventType = UniversalTriggerType.ChatCommand;
    public UniversalTriggerType SelectedEventType
    {
        get => selectedEventType;
        set
        {
            if (SetProperty(ref selectedEventType, value))
            {
                Draft.TriggerType = value;
                RaisePropertyChanged(nameof(IsChatCommandSelected));
                RaisePropertyChanged(nameof(IsChannelPointSelected));
                RaisePropertyChanged(nameof(IsBitsSelected));
                RaisePropertyChanged(nameof(IsSubscriptionSelected));
                RaisePropertyChanged(nameof(IsFollowSelected));
            }
        }
    }

    public bool IsChatCommandSelected => SelectedEventType == UniversalTriggerType.ChatCommand;
    public bool IsChannelPointSelected => SelectedEventType == UniversalTriggerType.ChannelPointReward;
    public bool IsBitsSelected => SelectedEventType == UniversalTriggerType.Bits;
    public bool IsSubscriptionSelected => SelectedEventType is UniversalTriggerType.Subscription or UniversalTriggerType.GiftSubscription;
    public bool IsFollowSelected => SelectedEventType == UniversalTriggerType.Follow;

    public UniversalTriggerRule Draft { get; } = new();

    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand BackCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand TestCommand { get; }

    public event Action? CloseRequested;
    public event Action<UniversalTriggerRule>? SaveRequested;

    public UniversalTriggerCreateWizardViewModel()
    {
        NextCommand = new AsyncRelayCommand(async () => { CurrentStep++; await Task.CompletedTask; });
        BackCommand = new AsyncRelayCommand(async () => { CurrentStep--; await Task.CompletedTask; });
        CancelCommand = new AsyncRelayCommand(async () => { CloseRequested?.Invoke(); await Task.CompletedTask; });
        SaveCommand = new AsyncRelayCommand(async () => { SaveRequested?.Invoke(Draft); await Task.CompletedTask; });
        TestCommand = new AsyncRelayCommand(async () => await Task.CompletedTask);
    }
}