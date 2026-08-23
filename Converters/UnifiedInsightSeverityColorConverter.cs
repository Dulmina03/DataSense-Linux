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
                UnifiedInsightSeverity.Critical => SemanticBrushConverter.Resolve("Danger"),
                UnifiedInsightSeverity.Warning  => SemanticBrushConverter.Resolve("Warning"),
                UnifiedInsightSeverity.Success  => SemanticBrushConverter.Resolve("Success"),
                _                               => SemanticBrushConverter.Resolve("Muted"),
            };
        }
        return SemanticBrushConverter.Resolve("Muted");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
