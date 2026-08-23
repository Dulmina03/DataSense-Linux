using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DataSense.Models;

namespace DataSense.Converters;

public class TrendColorConverter : IValueConverter
{
    public static readonly TrendColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string trend)
        {
            return trend switch
            {
                "Increasing" => SemanticBrushConverter.Resolve("Danger"),
                "Decreasing" => SemanticBrushConverter.Resolve("Success"),
                _ => SemanticBrushConverter.Resolve("Muted")
            };
        }
        return SemanticBrushConverter.Resolve("Muted");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class RunningStatusBgConverter : IValueConverter
{
    public static readonly RunningStatusBgConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRunning)
        {
            return isRunning ? SemanticBrushConverter.Resolve("SuccessSurface") : SemanticBrushConverter.Resolve("DangerSurface");
        }
        return SemanticBrushConverter.Resolve("DangerSurface");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class RunningStatusColorConverter : IValueConverter
{
    public static readonly RunningStatusColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRunning)
        {
            return isRunning ? SemanticBrushConverter.Resolve("Success") : SemanticBrushConverter.Resolve("Danger");
        }
        return SemanticBrushConverter.Resolve("Danger");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class RunningStatusTextConverter : IValueConverter
{
    public static readonly RunningStatusTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRunning)
        {
            return isRunning ? "Running" : "Exited";
        }
        return "Exited";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ProcessIndexColorConverter : IValueConverter
{
    public static readonly ProcessIndexColorConverter Instance = new();

    private static readonly IBrush[] Palette = new IBrush[]
    {
        new SolidColorBrush(Color.Parse("#38BDF8")), // Cyan
        new SolidColorBrush(Color.Parse("#10B981")), // Green
        new SolidColorBrush(Color.Parse("#EAB308")), // Yellow
        new SolidColorBrush(Color.Parse("#EF4444")), // Red
        new SolidColorBrush(Color.Parse("#A855F7")), // Purple
        new SolidColorBrush(Color.Parse("#F97316"))  // Orange
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int index = 0;
        if (parameter != null && int.TryParse(parameter.ToString(), out int pIdx))
        {
            index = pIdx;
        }
        else if (value is ApplicationHistoricalProfile profile)
        {
            index = Math.Abs(profile.ProcessName?.GetHashCode() ?? 0) % Palette.Length;
        }
        else if (value is double pct)
        {
            index = (int)(pct * 10) % Palette.Length;
        }
        else if (value is string str)
        {
            index = Math.Abs(str.GetHashCode()) % Palette.Length;
        }

        return Palette[Math.Abs(index) % Palette.Length];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
