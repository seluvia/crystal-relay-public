using System.Globalization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class FloatValueModeConverter
{
    public static bool TryParseNormalized(FloatValueMode mode, string? text, out double normalizedValue)
    {
        normalizedValue = 0;
        if (!double.TryParse(text?.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var rawValue))
        {
            return false;
        }

        normalizedValue = mode == FloatValueMode.Percent
            ? rawValue / 100d
            : rawValue;
        normalizedValue = Math.Clamp(normalizedValue, 0d, 1d);
        return true;
    }

    public static string ToOscText(double normalizedValue) =>
        Math.Clamp(normalizedValue, 0d, 1d).ToString("0.###", CultureInfo.InvariantCulture);

    public static string ToDisplayText(FloatValueMode mode, double normalizedValue)
    {
        var clampedValue = Math.Clamp(normalizedValue, 0d, 1d);
        return mode == FloatValueMode.Percent
            ? (clampedValue * 100d).ToString("0.##", CultureInfo.InvariantCulture)
            : clampedValue.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static string ConvertDisplayText(string? text, FloatValueMode sourceMode, FloatValueMode targetMode)
    {
        if (sourceMode == targetMode)
        {
            return text?.Trim() ?? string.Empty;
        }

        return TryParseNormalized(sourceMode, text, out var normalizedValue)
            ? ToDisplayText(targetMode, normalizedValue)
            : text?.Trim() ?? string.Empty;
    }
}
