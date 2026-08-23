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
                InsightSeverity.Info => SemanticBrushConverter.Resolve("Info"),
                InsightSeverity.Success => SemanticBrushConverter.Resolve("Success"),
                InsightSeverity.Warning => SemanticBrushConverter.Resolve("Warning"),
                InsightSeverity.Critical => SemanticBrushConverter.Resolve("Danger"),
                _ => SemanticBrushConverter.Resolve("Muted")
            };
        }
        return SemanticBrushConverter.Resolve("Muted");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
