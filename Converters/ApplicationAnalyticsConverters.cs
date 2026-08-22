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
                "Increasing" => SolidColorBrush.Parse("#FF4D4D"),
                "Decreasing" => SolidColorBrush.Parse("#00E676"),
                "Stable" => SolidColorBrush.Parse("#7777AA"),
                _ => SolidColorBrush.Parse("#555577")
            };
        }
        return SolidColorBrush.Parse("#555577");
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
            return isRunning ? SolidColorBrush.Parse("#162C21") : SolidColorBrush.Parse("#241C1C");
        }
        return SolidColorBrush.Parse("#241C1C");
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
            return isRunning ? SolidColorBrush.Parse("#00E676") : SolidColorBrush.Parse("#FF4D4D");
        }
        return SolidColorBrush.Parse("#FF4D4D");
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
