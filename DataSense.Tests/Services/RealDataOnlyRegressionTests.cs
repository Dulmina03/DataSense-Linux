using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class RealDataOnlyRegressionTests
{
    [Fact]
    public async Task EmptyDatabase_AnalyticsService_ReturnsZeroesWithoutMockData()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);

        var summary = await analytics.GetSummaryAsync(AnalyticsPeriod.Last30Days);

        Assert.Equal(0, summary.TotalDownloaded);
        Assert.Equal(0, summary.TotalUploaded);
        Assert.Equal(0, summary.AvgDailyBytes);
        Assert.Null(summary.PeakDay);
    }

    [Fact]
    public async Task EmptyDatabase_ForecastService_ReturnsHasSufficientDataFalse()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var forecast = new ForecastService(context.Repository, analytics);

        var result = await forecast.GetForecastAsync();

        Assert.False(result.HasSufficientData);
        Assert.Equal(0, result.CurrentUsageBytes);
        Assert.Equal(0, result.AverageDailyUsageBytes);
    }

    [Fact]
    public async Task EmptyDatabase_ApplicationIntelligence_ReturnsEstablishingBaselines()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntel = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        var recs = (await appIntel.GenerateApplicationRecommendationsAsync()).ToList();

        Assert.Single(recs);
        Assert.Equal("Establishing Application Baselines", recs[0].Title);
        Assert.Equal(0, recs[0].PotentialSavingsBytes);
    }
}
