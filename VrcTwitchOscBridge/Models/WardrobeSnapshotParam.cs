using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.Models;

public sealed class WardrobeSnapshotParam : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string parameterName = string.Empty;
    private OscParameterType parameterType = OscParameterType.Bool;
    private string setValue = "True";

    private static string T(string sourceText) => LocalizationService.Translate(sourceText);
    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public string ParameterName
    {
        get => parameterName;
        set
        {
            if (SetProperty(ref parameterName, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public OscParameterType ParameterType
    {
        get => parameterType;
        set
        {
            var normalizedValue = value is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float
                ? value
                : OscParameterType.Bool;
            if (SetProperty(ref parameterType, normalizedValue))
            {
                SetValue = normalizedValue switch
                {
                    OscParameterType.Bool => "True",
                    OscParameterType.Float => "0.0",
                    _ => "0"
                };
                RaisePropertyChanged(nameof(UsesBoolParameter));
                RaisePropertyChanged(nameof(UsesIntParameter));
                RaisePropertyChanged(nameof(UsesFloatParameter));
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public string SetValue
    {
        get => setValue;
        set
        {
            if (SetProperty(ref setValue, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public bool UsesBoolParameter => ParameterType == OscParameterType.Bool;
    public bool UsesIntParameter => ParameterType == OscParameterType.Int;
    public bool UsesFloatParameter => ParameterType == OscParameterType.Float;

    public string DisplaySummary
    {
        get
        {
            var param = string.IsNullOrWhiteSpace(ParameterName)
                ? T("Pick parameter")
                : ParameterName.Trim();
            var val = string.IsNullOrWhiteSpace(SetValue)
                ? T("Set value")
                : SetValue.Trim();
            return TF("{0} -> {1} ({2})", param, val, ParameterType);
        }
    }
}