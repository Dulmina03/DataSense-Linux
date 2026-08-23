using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

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
                "Stable" => SemanticBrushConverter.Resolve("Muted"),
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

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int index = 0;
        if (parameter != null && int.TryParse(parameter.ToString(), out int pIdx))
        {
            index = pIdx;
        }

        string key = (index % 5) switch
        {
            0 => "Brush.ChartSegment1",
            1 => "Brush.ChartSegment2",
            2 => "Brush.ChartSegment3",
            3 => "Brush.ChartSegment4",
            4 => "Brush.ChartSegment5",
            _ => "Brush.ChartSegment1"
        };

        if (Avalonia.Application.Current != null &&
            Avalonia.Application.Current.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out var resource) &&
            resource is IBrush brush)
        {
            return brush;
        }

        return SemanticBrushConverter.Resolve("Download");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
