using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Converters;

public sealed class ReturnAvatarModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ReturnAvatarMode mode && parameter is string paramName)
        {
            return string.Equals(mode.ToString(), paramName, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
