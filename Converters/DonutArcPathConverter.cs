using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DataSense.Models;

namespace DataSense.Converters;

/// <summary>
/// Converts process list percentages or download/upload ratio into geometric PathGeometry arc shapes.
/// Supports both Donut (hollow ring) and Pie (solid pie slice) geometries.
/// </summary>
public class DonutArcPathConverter : IMultiValueConverter
{
    public static readonly DonutArcPathConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2)
            return Geometry.Parse("M 0,0");

        double outerR = 74;
        double innerR = 52;
        double cx = 80;
        double cy = 80;
        bool isPieMode = false;

        if (parameter != null)
        {
            string pStr = parameter.ToString()!;
            if (pStr.StartsWith("Pie", StringComparison.OrdinalIgnoreCase))
            {
                isPieMode = true;
                innerR = 0;
                string[] parts = pStr.Split(',');
                if (parts.Length > 1 && double.TryParse(parts[1], out double pr))
                {
                    outerR = pr;
                    cx = pr + 4;
                    cy = pr + 4;
                }
            }
            else if (double.TryParse(pStr, out double r))
            {
                outerR = r;
                innerR = r * 0.7;
                cx = r + 4;
                cy = r + 4;
            }
        }

        double startPct = 0;
        double sweepPct = 0;

        // Case 1: Process list item (item, list)
        if (values[0] is ApplicationHistoricalProfile targetItem && values[1] is IEnumerable<ApplicationHistoricalProfile> list)
        {
            double cumulative = 0;
            bool found = false;
            foreach (var item in list)
            {
                if (item == targetItem)
                {
                    startPct = cumulative;
                    sweepPct = item.PercentageOfTotal;
                    found = true;
                    break;
                }
                cumulative += item.PercentageOfTotal;
            }
            if (!found || sweepPct <= 0)
                return Geometry.Parse("M 0,0");
        }
        // Case 2: Monthly ratio (isUploadFlag, downloadGridLength, uploadGridLength)
        else if (values.Count >= 3 && values[0] is bool isUpload && values[1] is GridLength dlGrid && values[2] is GridLength ulGrid)
        {
            double dlValue = dlGrid.Value;
            double ulValue = ulGrid.Value;
            double total = dlValue + ulValue;
            if (total <= 0) return Geometry.Parse("M 0,0");

            double dlPct = (dlValue / total) * 100.0;
            double ulPct = (ulValue / total) * 100.0;

            if (!isUpload)
            {
                startPct = 0;
                sweepPct = dlPct;
            }
            else
            {
                startPct = dlPct;
                sweepPct = ulPct;
            }
        }
        else
        {
            return Geometry.Parse("M 0,0");
        }

        if (sweepPct <= 0) return Geometry.Parse("M 0,0");

        // Clamp sweepPct to avoid full 360 circle arc segment degeneracy
        if (sweepPct >= 99.99) sweepPct = 99.9;

        double startAngle = (startPct / 100.0) * 360.0 - 90.0;
        double sweepAngle = (sweepPct / 100.0) * 360.0;
        double endAngle = startAngle + sweepAngle;

        double startRad = Math.PI * startAngle / 180.0;
        double endRad = Math.PI * endAngle / 180.0;

        double x1 = cx + outerR * Math.Cos(startRad);
        double y1 = cy + outerR * Math.Sin(startRad);
        double x2 = cx + outerR * Math.Cos(endRad);
        double y2 = cy + outerR * Math.Sin(endRad);

        bool isLargeArc = sweepAngle > 180.0;
        var pathGeometry = new PathGeometry();

        if (isPieMode || innerR <= 0)
        {
            // Solid Pie Chart Slice
            var figure = new PathFigure
            {
                StartPoint = new Point(cx, cy),
                IsClosed = true,
                IsFilled = true
            };

            figure.Segments!.Add(new LineSegment { Point = new Point(x1, y1) });
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(x2, y2),
                Size = new Size(outerR, outerR),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = isLargeArc
            });

            pathGeometry.Figures!.Add(figure);
        }
        else
        {
            // Donut Ring Slice
            double x3 = cx + innerR * Math.Cos(endRad);
            double y3 = cy + innerR * Math.Sin(endRad);
            double x4 = cx + innerR * Math.Cos(startRad);
            double y4 = cy + innerR * Math.Sin(startRad);

            var figure = new PathFigure
            {
                StartPoint = new Point(x1, y1),
                IsClosed = true,
                IsFilled = true
            };

            figure.Segments!.Add(new ArcSegment
            {
                Point = new Point(x2, y2),
                Size = new Size(outerR, outerR),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = isLargeArc
            });

            figure.Segments.Add(new LineSegment { Point = new Point(x3, y3) });

            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(x4, y4),
                Size = new Size(innerR, innerR),
                SweepDirection = SweepDirection.CounterClockwise,
                IsLargeArc = isLargeArc
            });

            pathGeometry.Figures!.Add(figure);
        }

        return pathGeometry;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
