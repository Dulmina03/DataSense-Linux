using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DataSense.Models;

namespace DataSense.Converters;

public class UnifiedInsightSeverityColorConverter : IValueConverter
{
    public static readonly UnifiedInsightSeverityColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is UnifiedInsightSeverity severity)
        {
            return severity switch
            {
                UnifiedInsightSeverity.Critical => new SolidColorBrush(Color.Parse("#FF3366")),
                UnifiedInsightSeverity.Warning  => new SolidColorBrush(Color.Parse("#FFB74D")),
                UnifiedInsightSeverity.Success  => new SolidColorBrush(Color.Parse("#00E676")),
                _                               => new SolidColorBrush(Color.Parse("#444466")),
            };
        }
        return new SolidColorBrush(Color.Parse("#444466"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
