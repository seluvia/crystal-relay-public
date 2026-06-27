using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VrcTwitchOscBridge.Converters;

public sealed class ReturnAvatarModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
