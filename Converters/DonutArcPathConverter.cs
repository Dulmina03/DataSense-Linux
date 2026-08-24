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
/// Converts process list percentages or download/upload ratio into mathematically precise,
/// true circular PathGeometry donut (hollow ring) or pie (solid slice) arc shapes.
/// </summary>
public class DonutArcPathConverter : IMultiValueConverter
{
    public static readonly DonutArcPathConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2)
            return new PathGeometry();

        // Standardized 200x200 drawing canvas defaults:
        // cx=100, cy=100, outerR=88, innerR=60 (28px ring thickness, 12px canvas margin)
        double outerR = 88;
        double innerR = 60;
        double cx = 100;
        double cy = 100;
        bool isPieMode = false;

        if (parameter != null)
        {
            string pStr = parameter.ToString()!;
            if (pStr.StartsWith("Pie", StringComparison.OrdinalIgnoreCase))
            {
                isPieMode = true;
                innerR = 0;
                string[] parts = pStr.Split(',');
                if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double pr))
                {
                    outerR = pr;
                    cx = pr + 12;
                    cy = pr + 12;
                }
            }
            else if (pStr.Contains(','))
            {
                string[] parts = pStr.Split(',');
                if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double or) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double ir))
                {
                    outerR = or;
                    innerR = ir;
                    cx = outerR + 12;
                    cy = outerR + 12;
                }
            }
            else if (double.TryParse(pStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double r))
            {
                outerR = r;
                innerR = r * 0.68;
                cx = r + 12;
                cy = r + 12;
            }
        }

        double startFraction = 0;
        double sweepFraction = 0;
        int totalItemCount = 1;

        // Case 1: Process list item (item, list)
        if (values[0] is ApplicationHistoricalProfile targetItem && values[1] is IEnumerable<ApplicationHistoricalProfile> list)
        {
            var itemList = new List<ApplicationHistoricalProfile>(list);
            totalItemCount = itemList.Count;

            double totalPctSum = 0;
            foreach (var item in itemList)
            {
                if (item.PercentageOfTotal > 0)
                    totalPctSum += item.PercentageOfTotal;
            }

            if (totalPctSum <= 0)
                return new PathGeometry();

            double cumulativePct = 0;
            bool found = false;
            foreach (var item in itemList)
            {
                if (item == targetItem || (item.ProcessName == targetItem.ProcessName && item.Pid == targetItem.Pid))
                {
                    startFraction = cumulativePct / totalPctSum;
                    sweepFraction = Math.Max(0, item.PercentageOfTotal) / totalPctSum;
                    found = true;
                    break;
                }
                if (item.PercentageOfTotal > 0)
                    cumulativePct += item.PercentageOfTotal;
            }

            if (!found || sweepFraction <= 0.0001)
                return new PathGeometry();
        }
        // Case 2: Monthly ratio (isUploadFlag, downloadGridLength, uploadGridLength)
        else if (values.Count >= 3 && values[0] is bool isUpload && values[1] is GridLength dlGrid && values[2] is GridLength ulGrid)
        {
            totalItemCount = 2;
            double dlValue = Math.Max(0, dlGrid.Value);
            double ulValue = Math.Max(0, ulGrid.Value);
            double total = dlValue + ulValue;

            if (total <= 0)
                return new PathGeometry();

            double dlFrac = dlValue / total;
            double ulFrac = ulValue / total;

            if (!isUpload)
            {
                startFraction = 0;
                sweepFraction = dlFrac;
            }
            else
            {
                startFraction = dlFrac;
                sweepFraction = ulFrac;
            }

            if (sweepFraction <= 0.0001)
                return new PathGeometry();
        }
        else
        {
            return new PathGeometry();
        }

        // Handle single 100% full donut circle
        if (sweepFraction >= 0.9999)
        {
            return CreateFullDonutGeometry(cx, cy, outerR, innerR, isPieMode);
        }

        // Angular math: 12 o'clock top is -90 degrees
        double rawStartAngle = (startFraction * 360.0) - 90.0;
        double rawSweepAngle = sweepFraction * 360.0;

        // Apply subtle segment gap if multiple segments exist
        double startAngle = rawStartAngle;
        double sweepAngle = rawSweepAngle;

        if (totalItemCount > 1)
        {
            double gapDeg = 2.0; // 2 degree gap for clean visual separation
            double effectiveGap = Math.Min(gapDeg, rawSweepAngle * 0.25);
            startAngle = rawStartAngle + (effectiveGap / 2.0);
            sweepAngle = rawSweepAngle - effectiveGap;
        }

        if (sweepAngle <= 0.01)
            return new PathGeometry();

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
            // Solid Pie slice
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
            // True Circular Donut Sector
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

            // 1. Clockwise outer circular arc
            figure.Segments!.Add(new ArcSegment
            {
                Point = new Point(x2, y2),
                Size = new Size(outerR, outerR),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = isLargeArc
            });

            // 2. Straight line to inner radius
            figure.Segments.Add(new LineSegment { Point = new Point(x3, y3) });

            // 3. Counter-clockwise inner circular arc
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(x4, y4),
                Size = new Size(innerR, innerR),
                SweepDirection = SweepDirection.CounterClockwise,
                IsLargeArc = isLargeArc
            });

            // IsClosed connects back to (x1, y1)
            pathGeometry.Figures!.Add(figure);
        }

        return pathGeometry;
    }

    private static Geometry CreateFullDonutGeometry(double cx, double cy, double outerR, double innerR, bool isPieMode)
    {
        var pathGeometry = new PathGeometry();

        if (isPieMode || innerR <= 0)
        {
            // Full circle pie
            var figure = new PathFigure
            {
                StartPoint = new Point(cx, cy - outerR),
                IsClosed = true,
                IsFilled = true
            };

            figure.Segments!.Add(new ArcSegment
            {
                Point = new Point(cx, cy + outerR),
                Size = new Size(outerR, outerR),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = false
            });
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(cx, cy - outerR),
                Size = new Size(outerR, outerR),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = false
            });

            pathGeometry.Figures!.Add(figure);
        }
        else
        {
            // Full continuous 360-degree donut ring
            var figure = new PathFigure
            {
                StartPoint = new Point(cx, cy - outerR),
                IsClosed = true,
                IsFilled = true
            };

            // Outer circle (top -> bottom -> top)
            figure.Segments!.Add(new ArcSegment
            {
                Point = new Point(cx, cy + outerR),
                Size = new Size(outerR, outerR),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = false
            });
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(cx, cy - outerR),
                Size = new Size(outerR, outerR),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = false
            });

            // Line down to inner radius at top
            figure.Segments.Add(new LineSegment { Point = new Point(cx, cy - innerR) });

            // Inner circle (top -> bottom -> top, counter-clockwise)
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(cx, cy + innerR),
                Size = new Size(innerR, innerR),
                SweepDirection = SweepDirection.CounterClockwise,
                IsLargeArc = false
            });
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(cx, cy - innerR),
                Size = new Size(innerR, innerR),
                SweepDirection = SweepDirection.CounterClockwise,
                IsLargeArc = false
            });

            pathGeometry.Figures!.Add(figure);
        }

        return pathGeometry;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
