using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DataSense.Models;

namespace DataSense.Converters;

public class InsightSeverityColorConverter : IValueConverter
{
    public static readonly InsightSeverityColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is InsightSeverity severity)
        {
            return severity switch
            {
                InsightSeverity.Info => new SolidColorBrush(Color.Parse("#00D2FF")),
                InsightSeverity.Success => new SolidColorBrush(Color.Parse("#00E676")),
                InsightSeverity.Warning => new SolidColorBrush(Color.Parse("#FFB300")),
                InsightSeverity.Critical => new SolidColorBrush(Color.Parse("#FF5252")),
                _ => new SolidColorBrush(Color.Parse("#888899"))
            };
        }
        return new SolidColorBrush(Color.Parse("#888899"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
