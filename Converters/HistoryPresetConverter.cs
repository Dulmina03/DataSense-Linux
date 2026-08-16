using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DataSense.ViewModels;

namespace DataSense.Converters;

/// <summary>
/// Maps <see cref="HistoryDatePreset"/> enum values to human-readable display strings
/// for the History page date-range ComboBox.
/// </summary>
public class HistoryPresetConverter : IValueConverter
{
    public static readonly HistoryPresetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is HistoryDatePreset preset)
        {
            return preset switch
            {
                HistoryDatePreset.Today      => "Today",
                HistoryDatePreset.Last7Days  => "Last 7 Days",
                HistoryDatePreset.Last30Days => "Last 30 Days",
                HistoryDatePreset.Custom     => "Custom Range",
                _                            => value.ToString()
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
                "Today"         => HistoryDatePreset.Today,
                "Last 7 Days"   => HistoryDatePreset.Last7Days,
                "Last 30 Days"  => HistoryDatePreset.Last30Days,
                "Custom Range"  => HistoryDatePreset.Custom,
                _               => HistoryDatePreset.Last7Days
            };
        }
        return HistoryDatePreset.Last7Days;
    }
}

/// <summary>
/// Returns true when the bound <see cref="HistoryDatePreset"/> equals <see cref="HistoryDatePreset.Custom"/>.
/// Used to show/hide the custom date picker row.
/// </summary>
public class HistoryPresetIsCustomConverter : IValueConverter
{
    public static readonly HistoryPresetIsCustomConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is HistoryDatePreset.Custom;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
