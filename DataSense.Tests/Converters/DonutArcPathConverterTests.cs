using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using DataSense.Converters;
using DataSense.Models;
using Xunit;

namespace DataSense.Tests.Converters;

public class DonutArcPathConverterTests
{
    [Fact]
    public void DonutArcPathConverter_SingleSegment100Percent_GeneratesValidFullGeometry()
    {
        var converter = DonutArcPathConverter.Instance;
        var p1 = new ApplicationHistoricalProfile { ProcessName = "brave", PercentageOfTotal = 100.0 };
        var list = new List<ApplicationHistoricalProfile> { p1 };

        var result = converter.Convert(new List<object?> { p1, list }, typeof(Geometry), null, CultureInfo.InvariantCulture);

        Assert.NotNull(result);
        var path = Assert.IsAssignableFrom<PathGeometry>(result);
        Assert.NotEmpty(path.Figures!);
        Assert.True(path.Figures![0].IsClosed);
    }

    [Fact]
    public void DonutArcPathConverter_TwoSegments_GeneratesValidGeometries()
    {
        var converter = DonutArcPathConverter.Instance;
        var p1 = new ApplicationHistoricalProfile { ProcessName = "brave", PercentageOfTotal = 60.0 };
        var p2 = new ApplicationHistoricalProfile { ProcessName = "code", PercentageOfTotal = 40.0 };
        var list = new List<ApplicationHistoricalProfile> { p1, p2 };

        var res1 = converter.Convert(new List<object?> { p1, list }, typeof(Geometry), null, CultureInfo.InvariantCulture);
        var res2 = converter.Convert(new List<object?> { p2, list }, typeof(Geometry), null, CultureInfo.InvariantCulture);

        Assert.NotNull(res1);
        Assert.NotNull(res2);

        var path1 = Assert.IsAssignableFrom<PathGeometry>(res1);
        var path2 = Assert.IsAssignableFrom<PathGeometry>(res2);

        Assert.NotEmpty(path1.Figures!);
        Assert.NotEmpty(path2.Figures!);

        // p1 is 60% (> 180 deg), so isLargeArc is true on outer arc
        var arc1 = Assert.IsAssignableFrom<ArcSegment>(path1.Figures![0].Segments![0]);
        Assert.True(arc1.IsLargeArc);

        // p2 is 40% (< 180 deg), so isLargeArc is false on outer arc
        var arc2 = Assert.IsAssignableFrom<ArcSegment>(path2.Figures![0].Segments![0]);
        Assert.False(arc2.IsLargeArc);
    }

    [Fact]
    public void DonutArcPathConverter_ZeroPercentSegment_ReturnsEmptyGeometry()
    {
        var converter = DonutArcPathConverter.Instance;
        var p1 = new ApplicationHistoricalProfile { ProcessName = "brave", PercentageOfTotal = 100.0 };
        var p2 = new ApplicationHistoricalProfile { ProcessName = "idle", PercentageOfTotal = 0.0 };
        var list = new List<ApplicationHistoricalProfile> { p1, p2 };

        var res = converter.Convert(new List<object?> { p2, list }, typeof(Geometry), null, CultureInfo.InvariantCulture);
        Assert.NotNull(res);

        // Result should be empty geometry
        if (res is PathGeometry pg)
        {
            Assert.Empty(pg.Figures!);
        }
    }

    [Fact]
    public void DonutArcPathConverter_MultipleSegments_GeneratesAllFiguresCorrectly()
    {
        var converter = DonutArcPathConverter.Instance;
        var profiles = new List<ApplicationHistoricalProfile>
        {
            new() { ProcessName = "brave", PercentageOfTotal = 40.0 },
            new() { ProcessName = "steam", PercentageOfTotal = 25.0 },
            new() { ProcessName = "code", PercentageOfTotal = 15.0 },
            new() { ProcessName = "discord", PercentageOfTotal = 10.0 },
            new() { ProcessName = "other", PercentageOfTotal = 10.0 }
        };

        foreach (var profile in profiles)
        {
            var res = converter.Convert(new List<object?> { profile, profiles }, typeof(Geometry), null, CultureInfo.InvariantCulture);
            Assert.NotNull(res);
            var path = Assert.IsAssignableFrom<PathGeometry>(res);
            Assert.NotEmpty(path.Figures!);
            Assert.True(path.Figures![0].IsClosed);
        }
    }

    [Fact]
    public void DonutArcPathConverter_MonthlyDownloadUploadRatio_GeneratesCorrectArcs()
    {
        var converter = DonutArcPathConverter.Instance;
        var dlGrid = new GridLength(75, GridUnitType.Star);
        var ulGrid = new GridLength(25, GridUnitType.Star);

        var dlRes = converter.Convert(new List<object?> { false, dlGrid, ulGrid }, typeof(Geometry), null, CultureInfo.InvariantCulture);
        var ulRes = converter.Convert(new List<object?> { true, dlGrid, ulGrid }, typeof(Geometry), null, CultureInfo.InvariantCulture);

        Assert.NotNull(dlRes);
        Assert.NotNull(ulRes);

        var dlPath = Assert.IsAssignableFrom<PathGeometry>(dlRes);
        var ulPath = Assert.IsAssignableFrom<PathGeometry>(ulRes);

        Assert.NotEmpty(dlPath.Figures!);
        Assert.NotEmpty(ulPath.Figures!);

        // Download (75%) has isLargeArc == true
        var dlArc = Assert.IsAssignableFrom<ArcSegment>(dlPath.Figures![0].Segments![0]);
        Assert.True(dlArc.IsLargeArc);

        // Upload (25%) has isLargeArc == false
        var ulArc = Assert.IsAssignableFrom<ArcSegment>(ulPath.Figures![0].Segments![0]);
        Assert.False(ulArc.IsLargeArc);
    }

    [Fact]
    public void DonutArcPathConverter_DuplicateProcessNamesWithDisplayIndex_GeneratesDistinctOffsets()
    {
        var converter = DonutArcPathConverter.Instance;
        var p1 = new ApplicationHistoricalProfile { ProcessName = "chrome", DisplayIndex = 0, PercentageOfTotal = 50.0 };
        var p2 = new ApplicationHistoricalProfile { ProcessName = "chrome", DisplayIndex = 1, PercentageOfTotal = 50.0 };
        var list = new List<ApplicationHistoricalProfile> { p1, p2 };

        var res1 = converter.Convert(new List<object?> { p1, list }, typeof(Geometry), null, CultureInfo.InvariantCulture);
        var res2 = converter.Convert(new List<object?> { p2, list }, typeof(Geometry), null, CultureInfo.InvariantCulture);

        Assert.NotNull(res1);
        Assert.NotNull(res2);

        var path1 = Assert.IsAssignableFrom<PathGeometry>(res1);
        var path2 = Assert.IsAssignableFrom<PathGeometry>(res2);

        Assert.NotEmpty(path1.Figures!);
        Assert.NotEmpty(path2.Figures!);

        // Start points must differ because one is at 0 degrees and the other is at 180 degrees
        Assert.NotEqual(path1.Figures![0].StartPoint, path2.Figures![0].StartPoint);
    }
}
