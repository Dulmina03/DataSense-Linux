using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media;
using DataSense.Converters;
using DataSense.Models;
using Xunit;

namespace DataSense.Tests.ViewModels;

public class DashboardDonutRealtimeTests
{
    [Fact]
    public void ApplicationHistoricalProfile_RaisesPropertyChanged_OnTodayBytesAndPercentageChange()
    {
        var profile = new ApplicationHistoricalProfile
        {
            ProcessName = "chrome",
            ApplicationDisplayName = "Google Chrome"
        };

        var changedProps = new List<string>();
        profile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        profile.TodayBytes = 52428800; // 50 MB
        profile.PercentageOfTotal = 75.5;

        Assert.Contains(nameof(ApplicationHistoricalProfile.TodayBytes), changedProps);
        Assert.Contains(nameof(ApplicationHistoricalProfile.PercentageOfTotal), changedProps);
        Assert.Contains(nameof(ApplicationHistoricalProfile.TooltipSummary), changedProps);
    }

    [Fact]
    public void DonutArcPathConverter_GeneratesValidArc_WithThreeBindings()
    {
        var item1 = new ApplicationHistoricalProfile
        {
            ProcessName = "chrome",
            TodayBytes = 600,
            PercentageOfTotal = 60.0,
            DisplayIndex = 0
        };

        var item2 = new ApplicationHistoricalProfile
        {
            ProcessName = "code",
            TodayBytes = 400,
            PercentageOfTotal = 40.0,
            DisplayIndex = 1
        };

        var list = new List<ApplicationHistoricalProfile> { item1, item2 };

        // Test with 3-element binding array: [item, list, item.PercentageOfTotal]
        var geom1 = DonutArcPathConverter.Instance.Convert(
            new object?[] { item1, list, item1.PercentageOfTotal },
            typeof(Geometry),
            null,
            CultureInfo.InvariantCulture) as PathGeometry;

        Assert.NotNull(geom1);
        Assert.NotEmpty(geom1.Figures);

        var geom2 = DonutArcPathConverter.Instance.Convert(
            new object?[] { item2, list, item2.PercentageOfTotal },
            typeof(Geometry),
            null,
            CultureInfo.InvariantCulture) as PathGeometry;

        Assert.NotNull(geom2);
        Assert.NotEmpty(geom2.Figures);
    }

    [Fact]
    public void DonutArcPathConverter_ReevaluatesCorrectly_WhenPercentagesShift()
    {
        var item1 = new ApplicationHistoricalProfile
        {
            ProcessName = "firefox",
            TodayBytes = 100,
            PercentageOfTotal = 100.0,
            DisplayIndex = 0
        };

        var list = new List<ApplicationHistoricalProfile> { item1 };

        var geomFull = DonutArcPathConverter.Instance.Convert(
            new object?[] { item1, list, item1.PercentageOfTotal },
            typeof(Geometry),
            null,
            CultureInfo.InvariantCulture) as PathGeometry;

        Assert.NotNull(geomFull);
        Assert.NotEmpty(geomFull.Figures);

        // Add a second item in real-time
        var item2 = new ApplicationHistoricalProfile
        {
            ProcessName = "curl",
            TodayBytes = 100,
            PercentageOfTotal = 50.0,
            DisplayIndex = 1
        };
        item1.PercentageOfTotal = 50.0;
        list.Add(item2);

        var geomHalf = DonutArcPathConverter.Instance.Convert(
            new object?[] { item1, list, item1.PercentageOfTotal },
            typeof(Geometry),
            null,
            CultureInfo.InvariantCulture) as PathGeometry;

        Assert.NotNull(geomHalf);
        Assert.NotEmpty(geomHalf.Figures);
    }
}
