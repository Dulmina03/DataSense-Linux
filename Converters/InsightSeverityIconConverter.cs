using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DataSense.Models;

namespace DataSense.Converters;

public class InsightSeverityIconConverter : IValueConverter
{
    public static readonly InsightSeverityIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is InsightSeverity severity)
        {
            return severity switch
            {
                InsightSeverity.Info => "ℹ️",
                InsightSeverity.Success => "✅",
                InsightSeverity.Warning => "⚠️",
                InsightSeverity.Critical => "🚨",
                _ => "💡"
            };
        }
        return "💡";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
