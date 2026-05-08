using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class MovementRedeemSet : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string name = "Default Movement Set";
    private ObservableCollection<TriggerRule> movementRules = [];

    public MovementRedeemSet()
    {
        movementRules.CollectionChanged += OnMovementRulesChanged;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, string.IsNullOrWhiteSpace(value) ? "Default Movement Set" : value.Trim()))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public ObservableCollection<TriggerRule> MovementRules
    {
        get => movementRules;
        set
        {
            if (ReferenceEquals(movementRules, value))
            {
                return;
            }

            movementRules.CollectionChanged -= OnMovementRulesChanged;
            if (SetProperty(ref movementRules, value ?? []))
            {
                movementRules.CollectionChanged += OnMovementRulesChanged;
                RaisePropertyChanged(nameof(MovementCountText));
            }
        }
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Name) ? "Default Movement Set" : Name;

    public string MovementCountText => MovementRules.Count == 1
        ? "1 movement redeem"
        : $"{MovementRules.Count} movement redeems";

    private void OnMovementRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(MovementCountText));
    }
}
