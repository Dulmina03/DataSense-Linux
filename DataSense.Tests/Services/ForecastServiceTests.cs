using System;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class ForecastServiceTests
{
    [Fact]
    public async Task GetForecastAsync_InsufficientData_ReturnsHasSufficientDataFalse()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var forecastService = new ForecastService(context.Repository, analytics);

        var forecast = await forecastService.GetForecastAsync();

        Assert.NotNull(forecast);
        Assert.False(forecast.HasSufficientData);
    }

    [Fact]
    public async Task GetForecastAsync_WithSufficientHistory_CalculatesEWMAForecast()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var forecastService = new ForecastService(context.Repository, analytics);

        DateTime today = DateTime.UtcNow.Date;
        long mb = 1024 * 1024;

        // Seed 5 days of usage
        for (int i = 5; i >= 1; i--)
        {
            DateTime day = today.AddDays(-i);
            long cumulativeRx = (6 - i) * 100 * mb;
            long cumulativeTx = (6 - i) * 50 * mb;
            await TestDataBuilder.SeedCumulativeUsageAsync(
                context.Repository,
                "wlan0",
                day.AddHours(2),
                TimeSpan.FromHours(1),
                ((6 - i - 1) * 100 * mb, (6 - i - 1) * 50 * mb),
                (cumulativeRx, cumulativeTx)
            );
        }

        var forecast = await forecastService.GetForecastAsync();

        Assert.NotNull(forecast);
        Assert.True(forecast.HasSufficientData);
        Assert.True(forecast.AverageDailyUsageBytes > 0);
        Assert.True(forecast.ProjectedMonthEndBytes >= forecast.CurrentUsageBytes);
        Assert.True(forecast.LowerBoundBytes <= forecast.ProjectedMonthEndBytes);
        Assert.True(forecast.UpperBoundBytes >= forecast.ProjectedMonthEndBytes);
    }

    [Fact]
    public async Task Cache_Behavior_InvalidateOnSaveBudget()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var forecastService = new ForecastService(context.Repository, analytics);

        var budget1 = await forecastService.GetBudgetAsync();
        Assert.False(budget1.Enabled);

        var newBudget = new DataBudget
        {
            Enabled = true,
            MonthlyLimitBytes = 50 * 1024 * 1024 * 1024L,
            WarningThresholdPct = 80,
            CriticalThresholdPct = 95
        };

        await forecastService.SaveBudgetAsync(newBudget);
        var updatedBudget = await forecastService.GetBudgetAsync();

        Assert.True(updatedBudget.Enabled);
        Assert.Equal(50 * 1024 * 1024 * 1024L, updatedBudget.MonthlyLimitBytes);
    }
}
