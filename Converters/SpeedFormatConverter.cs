using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DataSense.Helpers;

namespace DataSense.Converters;

/// <summary>
/// Converts a raw rate (double) to a human-readable string like "2.4 MB/s".
/// </summary>
public class SpeedFormatConverter : IValueConverter
{
    public static readonly SpeedFormatConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double rate)
            return ByteFormatter.FormatSpeed(rate);
        return "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
