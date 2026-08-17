using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DataSense.Helpers;

namespace DataSense.Converters;

/// <summary>
/// Converts a raw byte count (long) to a human-readable string like "24.6 MB".
/// </summary>
public class ByteFormatConverter : IValueConverter
{
    public static readonly ByteFormatConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes)
            return ByteFormatter.FormatBytes(bytes);
        if (value is int intBytes)
            return ByteFormatter.FormatBytes(intBytes);
        return "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
