using System.Globalization;
using System.Windows.Data;

namespace VrcTwitchOscBridge;

[ValueConversion(typeof(string), typeof(bool))]
public sealed class UniversalTriggerBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return false;
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (bool.TryParse(text, out var parsed))
        {
            return parsed;
        }

        if (text == "1")
        {
            return true;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "True" : "False";
    }
}