using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DataSense.Models;
using DataSense.Services;

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

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ApplicationHistoricalProfile profile)
        {
            if (profile.DisplayIndex >= 0)
                return ApplicationChartColorProvider.Instance.GetColorBrushByIndex(profile.DisplayIndex);
            return ApplicationChartColorProvider.Instance.GetColorBrush(profile.ProcessName);
        }
        if (value is int idx)
        {
            return ApplicationChartColorProvider.Instance.GetColorBrushByIndex(idx);
        }
        if (value is string str)
        {
            if (int.TryParse(str, out int parsedIdx))
                return ApplicationChartColorProvider.Instance.GetColorBrushByIndex(parsedIdx);
            return ApplicationChartColorProvider.Instance.GetColorBrush(str);
        }
        if (parameter != null && int.TryParse(parameter.ToString(), out int pIdx))
        {
            return ApplicationChartColorProvider.Instance.GetColorBrushByIndex(pIdx);
        }

        return ApplicationChartColorProvider.Instance.GetColorBrush(value?.ToString());
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ProcessIndexGradientBrushConverter : IValueConverter
{
    public static readonly ProcessIndexGradientBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ApplicationHistoricalProfile profile)
        {
            if (profile.DisplayIndex >= 0)
                return ApplicationChartColorProvider.Instance.GetGradientBrushByIndex(profile.DisplayIndex);
            return ApplicationChartColorProvider.Instance.GetGradientBrush(profile.ProcessName);
        }
        if (value is int idx)
        {
            return ApplicationChartColorProvider.Instance.GetGradientBrushByIndex(idx);
        }
        if (value is string str)
        {
            if (int.TryParse(str, out int parsedIdx))
                return ApplicationChartColorProvider.Instance.GetGradientBrushByIndex(parsedIdx);
            return ApplicationChartColorProvider.Instance.GetGradientBrush(str);
        }
        if (parameter != null && int.TryParse(parameter.ToString(), out int pIdx))
        {
            return ApplicationChartColorProvider.Instance.GetGradientBrushByIndex(pIdx);
        }

        return ApplicationChartColorProvider.Instance.GetGradientBrush(value?.ToString());
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
