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
