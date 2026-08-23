using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DataSense.ViewModels;

namespace DataSense.Converters;

/// <summary>
/// Converts a collection of DailyChartBarViewModel into a smooth dynamic PathGeometry
/// for real data-driven area wave rendering in Avalonia chart controls.
/// </summary>
public class ChartAreaGeometryConverter : IValueConverter
{
    public static readonly ChartAreaGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<DailyChartBarViewModel> itemsCollection)
            return Geometry.Parse("M 0,195 L 600,195 Z");

        var items = itemsCollection.ToList();
        if (items.Count == 0)
            return Geometry.Parse("M 0,195 L 600,195 Z");

        double canvasHeight = 195.0;
        if (parameter != null && double.TryParse(parameter.ToString(), out double h))
        {
            canvasHeight = h;
        }

        var points = new List<Point>();
        double lastX = 0;

        foreach (var item in items)
        {
            double cx = item.BarX + (item.BarWidth / 2.0);
            double cy = canvasHeight - item.DownloadBarHeight;
            if (cy < 10) cy = 10;
            if (cy > canvasHeight) cy = canvasHeight;

            points.Add(new Point(cx, cy));
            lastX = Math.Max(lastX, item.BarX + item.BarWidth + 20);
        }

        if (points.Count == 1)
        {
            points.Insert(0, new Point(0, points[0].Y));
            points.Add(new Point(points[points.Count - 1].X + 100, points[points.Count - 1].Y));
        }

        var pathGeometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, points.First().Y),
            IsClosed = true,
            IsFilled = true
        };

        // Quadratic Bézier segments (Point1 = control point, Point2 = end point)
        for (int i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[i];
            var p1 = points[i + 1];
            double midX = (p0.X + p1.X) / 2.0;

            figure.Segments!.Add(new QuadraticBezierSegment
            {
                Point1 = new Point(midX, p0.Y),
                Point2 = p1
            });
        }

        double endX = Math.Max(lastX, points.Last().X);
        figure.Segments!.Add(new LineSegment { Point = new Point(endX, canvasHeight) });
        figure.Segments.Add(new LineSegment { Point = new Point(0, canvasHeight) });

        pathGeometry.Figures!.Add(figure);
        return pathGeometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
