using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class UniversalTriggerAction : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string oscAddress = string.Empty;
    private UniversalTriggerValueKind valueKind = UniversalTriggerValueKind.Int;
    private string targetValue = string.Empty;
    private string defaultValue = string.Empty;
    private double durationSeconds;
    private bool addToQueue = true;
    private string importGroupKey = string.Empty;

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public string OscAddress
    {
        get => oscAddress;
        set
        {
            if (SetProperty(ref oscAddress, value?.Trim() ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public UniversalTriggerValueKind ValueKind
    {
        get => valueKind;
        set
        {
            if (SetProperty(ref valueKind, Enum.IsDefined(value) ? value : UniversalTriggerValueKind.Int))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public string TargetValue
    {
        get => targetValue;
        set
        {
            if (SetProperty(ref targetValue, value?.Trim() ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public string DefaultValue
    {
        get => defaultValue;
        set => SetProperty(ref defaultValue, value?.Trim() ?? string.Empty);
    }

    public double DurationSeconds
    {
        get => durationSeconds;
        set
        {
            var normalizedValue = double.IsFinite(value) ? Math.Max(0, value) : 0;
            if (SetProperty(ref durationSeconds, normalizedValue))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public bool AddToQueue
    {
        get => addToQueue;
        set
        {
            if (SetProperty(ref addToQueue, value))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public string ImportGroupKey
    {
        get => importGroupKey;
        set => SetProperty(ref importGroupKey, value?.Trim() ?? string.Empty);
    }

    public string DisplaySummary
    {
        get
        {
            var address = string.IsNullOrWhiteSpace(OscAddress) ? "Set OSC address" : OscAddress;
            var queueText = AddToQueue ? "queued" : "parallel";
            return $"{address} -> {TargetValue} ({DurationSeconds:0.##}s, {queueText})";
        }
    }
}
