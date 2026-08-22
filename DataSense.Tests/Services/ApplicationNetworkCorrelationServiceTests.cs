using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Moq;
using Xunit;

namespace DataSense.Tests.Services;

public class ApplicationNetworkCorrelationServiceTests
{
    [Fact]
    public async Task GetNetworkApplicationBreakdownAsync_WithEmptyDatabase_ReturnsEmptyBreakdown()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var patternMock = new Mock<IPatternAnalysisService>();
        var forecastMock = new Mock<IForecastService>();
        var eventMock = new Mock<IEventService>();

        var correlationService = new ApplicationNetworkCorrelationService(
            context.Repository,
            patternMock.Object,
            forecastMock.Object,
            eventMock.Object
        );

        var breakdown = await correlationService.GetNetworkApplicationBreakdownAsync("WIFI-Home", AnalyticsPeriod.Last7Days);

        Assert.NotNull(breakdown);
        Assert.Equal("—", breakdown.TopApplication);
        Assert.Equal(0, breakdown.TotalAttributedTraffic);
        Assert.Empty(breakdown.Profiles);
        Assert.False(breakdown.HasProfiles);
    }

    [Fact]
    public async Task GetHotspotIntelligenceAsync_WithNoHotspotTelemetry_ReturnsNonHotspotInfo()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var patternMock = new Mock<IPatternAnalysisService>();
        var forecastMock = new Mock<IForecastService>();
        var eventMock = new Mock<IEventService>();

        var correlationService = new ApplicationNetworkCorrelationService(
            context.Repository,
            patternMock.Object,
            forecastMock.Object,
            eventMock.Object
        );

        var hotspot = await correlationService.GetHotspotIntelligenceAsync("Ethernet-Work");

        Assert.NotNull(hotspot);
        Assert.False(hotspot.IsHotspot);
        Assert.Empty(hotspot.TopHotspotConsumers);
        Assert.Equal(0.0, hotspot.ConcentrationPercentage);
    }

    [Fact]
    public async Task GetBudgetCorrelationAsync_WithNoBudgetsConfigured_ReturnsEmptyBudgetCorrelation()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var patternMock = new Mock<IPatternAnalysisService>();
        var forecastMock = new Mock<IForecastService>();
        var eventMock = new Mock<IEventService>();

        forecastMock.Setup(f => f.GetBudgetAsync()).ReturnsAsync(new DataBudget { Enabled = false, MonthlyLimitBytes = 0 });
        forecastMock.Setup(f => f.GetForecastAsync()).ReturnsAsync(new UsageForecast { HasSufficientData = false });

        var correlationService = new ApplicationNetworkCorrelationService(
            context.Repository,
            patternMock.Object,
            forecastMock.Object,
            eventMock.Object
        );

        var budgetCorr = await correlationService.GetBudgetCorrelationAsync();

        Assert.NotNull(budgetCorr);
        Assert.Equal("—", budgetCorr.ProjectedApplicationContribution);
        Assert.Empty(budgetCorr.OveruseDrivers);
        Assert.False(budgetCorr.HasOverageRisk);
    }

    [Fact]
    public async Task GetNetworkSpecificInsightsAsync_WithEmptyDatabase_ReturnsEmptyList()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var patternMock = new Mock<IPatternAnalysisService>();
        var forecastMock = new Mock<IForecastService>();
        var eventMock = new Mock<IEventService>();

        var correlationService = new ApplicationNetworkCorrelationService(
            context.Repository,
            patternMock.Object,
            forecastMock.Object,
            eventMock.Object
        );

        var insights = await correlationService.GetNetworkSpecificInsightsAsync("VPN-Secure");

        Assert.NotNull(insights);
        Assert.Empty(insights);
    }
}
