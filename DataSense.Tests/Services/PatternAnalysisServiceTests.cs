using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class PatternAnalysisServiceTests
{
    [Fact]
    public async Task InsufficientHistory_ReturnsNoAnomalies()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);

        var anomalies = (await patternService.DetectAnomaliesAsync()).ToList();

        Assert.Empty(anomalies);
    }

    [Fact]
    public async Task GetHourlyPatternsAsync_WithInsufficientDays_ReturnsHasSufficientDataFalse()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);

        var patterns = await patternService.GetHourlyPatternsAsync();

        Assert.NotNull(patterns);
        Assert.Equal(24, patterns.Count);
        Assert.False(patterns[0].Pattern.HasSufficientData);
    }
}
