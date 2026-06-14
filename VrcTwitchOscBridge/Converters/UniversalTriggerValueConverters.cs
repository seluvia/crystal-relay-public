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

[ValueConversion(typeof(string), typeof(string))]
public sealed class UniversalTriggerIntConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return "0";
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return "0";
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue.ToString(CultureInfo.InvariantCulture);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
            && doubleValue == Math.Truncate(doubleValue)
            && doubleValue >= int.MinValue
            && doubleValue <= int.MaxValue)
        {
            return ((int)doubleValue).ToString(CultureInfo.InvariantCulture);
        }

        return text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return 0;
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}

[ValueConversion(typeof(string), typeof(string))]
public sealed class UniversalTriggerFloatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return "0";
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return "0";
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            if (doubleValue == Math.Truncate(doubleValue)
                && !double.IsInfinity(doubleValue)
                && doubleValue >= -1e15
                && doubleValue <= 1e15)
            {
                return ((long)doubleValue).ToString(CultureInfo.InvariantCulture);
            }

            return doubleValue.ToString("R", CultureInfo.InvariantCulture);
        }

        return text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return 0.0;
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return 0.0;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0.0;
    }
}