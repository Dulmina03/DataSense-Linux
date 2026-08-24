using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DataSense.ViewModels;

namespace DataSense.Converters;

/// <summary>
/// Maps <see cref="HistoryPeriodType"/> enum values to human-readable display strings.
/// </summary>
public class HistoryPresetConverter : IValueConverter
{
    public static readonly HistoryPresetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is HistoryPeriodType period)
        {
            return period switch
            {
                HistoryPeriodType.Today     => "Today",
                HistoryPeriodType.Last7Days => "Last 7 Days",
                HistoryPeriodType.Month     => "Month",
                _                           => value.ToString()
            };
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return str switch
            {
                "Today"       => HistoryPeriodType.Today,
                "Last 7 Days" => HistoryPeriodType.Last7Days,
                "Month"       => HistoryPeriodType.Month,
                _             => HistoryPeriodType.Last7Days
            };
        }
        return HistoryPeriodType.Last7Days;
    }
}
