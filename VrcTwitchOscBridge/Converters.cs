using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VrcTwitchOscBridge.Models;

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
        if (values is null || values.Length == 0)
        {
            return "\u2014";
        }

        string? mode = values[0] switch
        {
            AvatarScaleMode modeEnum => modeEnum.ToString(),
            string modeStr => modeStr,
            _ => null
        };

        if (mode is null)
        {
            return "\u2014";
        }

        TryGetDouble(values[6], out var cu);
        if (cu <= 0) cu = 1.6;
        var currentPrefix = string.Format(culture, "Your current height is {0:0.##}m. ", cu);

        return mode switch
        {
            "SetHeight" => values.Length >= 2 && TryGetDouble(values[1], out var h)
                ? string.Format(culture, "{0}Sets avatar height to {1:0.##}m.", currentPrefix, h)
                : string.Format(culture, "{0}Sets avatar height directly.", currentPrefix),
            "RandomHeight" or "GlitchyRandomHeight" =>
                values.Length >= 4 && TryGetDouble(values[2], out var lo) && TryGetDouble(values[3], out var hi)
                    ? string.Format(culture, mode == "GlitchyRandomHeight"
                        ? "{0}Rapidly rolls random heights between {1:0.##}m and {2:0.##}m with a {3:0.##}s transition between each jump, until Active Time ends."
                        : "{0}Each trigger rolls a random height between {1:0.##}m and {2:0.##}m.", currentPrefix, lo, hi,
                        values.Length >= 5 ? values[4] : null)
                    : "\u2014",
            "RelativeHeight" =>
                values.Length >= 7 && TryGetDouble(values[5], out var ch) && TryGetDouble(values[6], out var relCu)
                    ? string.Format(culture, values.Length >= 12 && values[11] is true
                        ? "{0}Subtracts {1:0.##}m, changing height to {2:0.##}m."
                        : "{0}Adds {1:0.##}m, changing height to {2:0.##}m.", currentPrefix, ch, relCu + (values.Length >= 12 && values[11] is true ? -ch : ch))
                    : "\u2014",
            "Multiplier" =>
                values.Length >= 8 && TryGetDouble(values[7], out var mul) && TryGetDouble(values[6], out var mulCu) && values[8] is bool divide
                    ? string.Format(culture, "{0}Multiplies height by {1}{2:0.##}, changing to {3:0.##}m.",
                        currentPrefix, divide ? "\u00F7" : "\u00D7", mul, divide && mul != 0 ? mulCu / mul : mulCu * mul)
                    : "\u2014",
            "Preset" =>
                values.Length >= 11 && values[9] is AvatarScalePreset presetLabel && TryGetDouble(values[10], out var ph)
                    ? string.Format(culture, "{0}Sets avatar height to {1} preset ({2:0.##}m).", currentPrefix, presetLabel, ph)
                    : "\u2014",
            _ => "\u2014"
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

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var inverted = parameter is string s && s.Equals("Inverted", StringComparison.OrdinalIgnoreCase);
        var visible = inverted ? count == 0 : count > 0;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class StepGreaterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int step) return false;
        if (parameter is null || !int.TryParse(parameter.ToString(), out var threshold)) return false;
        return step > threshold;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class StepVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int step) return Visibility.Collapsed;
        if (parameter is null || !int.TryParse(parameter.ToString(), out var target)) return Visibility.Collapsed;
        return step == target ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(string))]
public sealed class CollapseChevronTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "▾" : "▴";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(System.Windows.Media.Brush))]
public sealed class CollapseChevronColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true
            ? System.Windows.Application.Current?.TryFindResource("MutedBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray
            : System.Windows.Application.Current?.TryFindResource("AccentBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Purple;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class GuidToRuleNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Guid id) return "(unknown rule)";
        // Simple approach: show truncated GUID for now
        // Full rule name resolution would require access to the rule collection at convert time
        return id.ToString()[..8];
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(System.Windows.HorizontalAlignment))]
public sealed class BoolToHorizontalAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(System.Windows.Thickness))]
public sealed class BoolToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? new System.Windows.Thickness(0, 0, 3, 0) : new System.Windows.Thickness(3, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


[ValueConversion(typeof(object), typeof(System.Windows.Media.Brush))]
public sealed class GuidToSelectedOutfitBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Models.WardrobeOutfit selected) return System.Windows.Media.Brushes.Transparent;
        if (parameter is not Models.WardrobeOutfit item) return System.Windows.Media.Brushes.Transparent;
        return ReferenceEquals(selected, item)
            ? System.Windows.Application.Current?.TryFindResource("AccentBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Purple
            : System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(object[]), typeof(System.Windows.Media.Brush))]
public sealed class IsSelectedOutfitBrushMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
        if (values[0] is not Models.WardrobeOutfit selected) return System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
        if (values[1] is not Models.WardrobeOutfit item) return System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
        return ReferenceEquals(selected, item)
            ? System.Windows.Application.Current?.TryFindResource("AccentBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Purple
            : System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IsSelectedOutfitThicknessMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return new System.Windows.Thickness(1);
        if (values[0] is not Models.WardrobeOutfit selected) return new System.Windows.Thickness(1);
        if (values[1] is not Models.WardrobeOutfit item) return new System.Windows.Thickness(1);
        return ReferenceEquals(selected, item)
            ? new System.Windows.Thickness(2)
            : new System.Windows.Thickness(1);
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IsSelectedOutfitBoolMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        if (values[0] is not Models.WardrobeOutfit selected) return false;
        if (values[1] is not Models.WardrobeOutfit item) return false;
        return ReferenceEquals(selected, item);
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(object[]), typeof(System.Windows.Media.Brush))]
public sealed class IsSelectedRuleBrushMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
        if (values[0] is not Models.TriggerRule selected) return System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
        if (values[1] is not Models.TriggerRule item) return System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
        return ReferenceEquals(selected, item)
            ? System.Windows.Application.Current?.TryFindResource("AccentBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Purple
            : System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IsSelectedRuleThicknessMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return new System.Windows.Thickness(1);
        if (values[0] is not Models.TriggerRule selected) return new System.Windows.Thickness(1);
        if (values[1] is not Models.TriggerRule item) return new System.Windows.Thickness(1);
        return ReferenceEquals(selected, item)
            ? new System.Windows.Thickness(2)
            : new System.Windows.Thickness(1);
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
