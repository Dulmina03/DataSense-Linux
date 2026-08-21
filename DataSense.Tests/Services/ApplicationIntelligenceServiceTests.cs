using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class ApplicationIntelligenceServiceTests
{
    [Fact]
    public async Task GenerateRecommendations_WithEmptyData_ReturnsBaselineRecommendation()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        var recs = (await appIntelService.GenerateApplicationRecommendationsAsync()).ToList();

        Assert.Single(recs);
        Assert.Equal("Establishing Application Baselines", recs[0].Title);
        Assert.Equal(RecommendationImpact.Low, recs[0].Impact);
    }

    [Fact]
    public async Task GenerateRecommendations_WithDeterministicHighConsumer_ReturnsDeterministicHighBandwidthRecommendation()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        long heavyBytes = 500_000_000; // 500 MB

        // Seed 4 days of history so HasSufficientData is true
        for (int i = 4; i >= 0; i--)
        {
            DateTime day = now.AddDays(-i);
            await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "chrome", day, heavyBytes, 50_000_000);
        }

        var recs1 = (await appIntelService.GenerateApplicationRecommendationsAsync()).ToList();
        var recs2 = (await appIntelService.GenerateApplicationRecommendationsAsync()).ToList();

        Assert.NotEmpty(recs1);
        Assert.Equal(recs1.Count, recs2.Count);
        Assert.Equal(recs1[0].Title, recs2[0].Title);
        Assert.Equal(recs1[0].PotentialSavingsBytes, recs2[0].PotentialSavingsBytes);
    }
}
