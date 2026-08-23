using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DataSense.Converters;

public class SemanticBrushConverter : IValueConverter
{
    public static readonly SemanticBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string semanticKey)
        {
            var resourceKey = semanticKey switch
            {
                "Success" => "Brush.Success",
                "Warning" => "Brush.Warning",
                "Danger"  => "Brush.Danger",
                "Info"    => "Brush.Accent",
                "Muted"   => "Brush.TextMuted",
                "Download" => "Brush.Download",
                "Upload"  => "Brush.Upload",
                _         => "Brush.TextPrimary"
            };

            if (Application.Current != null && Application.Current.TryGetResource(resourceKey, out var res) && res is IBrush brush)
            {
                return brush;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
