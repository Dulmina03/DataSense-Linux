using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DataSense.Converters;

public class SemanticBrushConverter : IValueConverter
{
    public static readonly SemanticBrushConverter Instance = new();

    public static IBrush? Resolve(string semanticKey)
    {
        var resourceKey = semanticKey switch
        {
            "Success"   => "Brush.Success",
            "Warning"   => "Brush.Warning",
            "Danger"    => "Brush.Danger",
            "Info"      => "Brush.Accent",
            "Muted"     => "Brush.TextMuted",
            "Neutral"   => "Brush.TextSecondary",
            "Download"  => "Brush.Download",
            "Upload"    => "Brush.Upload",

            "SuccessSurface" => "Brush.SuccessSurface",
            "DangerSurface"  => "Brush.DangerSurface",

            _ => "Brush.TextPrimary"
        };

        if (Application.Current != null &&
            Application.Current.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var resource) &&
            resource is IBrush brush)
        {
            return brush;
        }

        return null;
    }

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is string semanticKey)
        {
            return Resolve(semanticKey);
        }

        return null;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
