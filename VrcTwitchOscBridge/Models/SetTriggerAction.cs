using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.Models;

public sealed class SetTriggerAction : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string parameterName = "VRCEmote";
    private OscParameterType parameterType = OscParameterType.Int;
    private string parameterValue = "1";

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
                : OscParameterType.Int;
            if (SetProperty(ref parameterType, normalizedValue))
            {
                ParameterValue = normalizedValue switch
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

    public string ParameterValue
    {
        get => parameterValue;
        set
        {
            if (SetProperty(ref parameterValue, value ?? string.Empty))
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
            var parameter = string.IsNullOrWhiteSpace(ParameterName)
                ? T("Pick parameter")
                : ParameterName.Trim();
            var value = string.IsNullOrWhiteSpace(ParameterValue)
                ? T("Set value")
                : ParameterValue.Trim();
            return TF("{0} -> {1} ({2})", parameter, value, ParameterType);
        }
    }
}
