using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VrcTwitchOscBridge;

public sealed class EnumBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return false;
        }

        var enumValue = value.ToString();
        var targetValue = parameter.ToString();
        return string.Equals(enumValue, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
        {
            return Enum.Parse(targetType, parameter.ToString()!);
        }

        return DependencyProperty.UnsetValue;
    }
}

public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return Visibility.Collapsed;
        }

        var enumValue = value.ToString();
        var targetValue = parameter.ToString();
        return string.Equals(enumValue, targetValue, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ScalePreviewConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length == 0 || values[0] is not string mode)
        {
            return "—";
        }

        return mode switch
        {
            "SetHeight" => values.Length >= 2 && TryGetDouble(values[1], out var h)
                ? string.Format(culture, "Sets the avatar height directly to {0:0.##}m.", h)
                : "Sets the avatar height directly.",
            "RandomHeight" or "GlitchyRandomHeight" =>
                values.Length >= 3 && TryGetDouble(values[1], out var lo) && TryGetDouble(values[2], out var hi)
                    ? string.Format(culture, mode == "GlitchyRandomHeight"
                        ? "Rapidly rolls random heights between {0:0.##}m and {1:0.##}m with a {2:0.##}s transition between each jump, until Active Time ends."
                        : "Each trigger rolls a random height between {0:0.##}m and {1:0.##}m.", lo, hi,
                        values.Length >= 4 ? values[3] : null)
                    : "—",
            "RelativeHeight" =>
                values.Length >= 3 && TryGetDouble(values[1], out var ch) && TryGetDouble(values[2], out var cu)
                    ? string.Format(culture, "Adds {0:+0.##;-0.##;0}m to the current height, going from {1:0.##}m to {2:0.##}m.", ch, cu, cu + ch)
                    : "—",
            "Multiplier" =>
                values.Length >= 4 && TryGetDouble(values[1], out var mul) && TryGetDouble(values[2], out var mcu) && values[3] is bool divide
                    ? string.Format(culture, divide
                        ? "Going from {0:0.##}m to {1:0.##}m using ÷{2:0.##}."
                        : "Going from {0:0.##}m to {1:0.##}m using ×{2:0.##}.", mcu, divide && mul != 0 ? mcu / mul : mcu * mul, mul)
                    : "—",
            "Preset" =>
                values.Length >= 3 && values[1] is string label && TryGetDouble(values[2], out var h2)
                    ? string.Format(culture, "Sets the avatar height to the {0} preset, which is {1:0.##}m.", label, h2)
                    : "—",
            _ => "—"
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryGetDouble(object value, out double result)
    {
        result = 0;
        if (value is null) return false;
        try { result = System.Convert.ToDouble(value, CultureInfo.InvariantCulture); return true; }
        catch { return false; }
    }
}