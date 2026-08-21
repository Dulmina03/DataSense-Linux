using System;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class BudgetTests
{
    [Fact]
    public async Task GetBudgetResultAsync_DisabledBudget_ReturnsNull()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var forecastService = new ForecastService(context.Repository, analytics);

        var budget = new DataBudget { Enabled = false, MonthlyLimitBytes = 100 * 1024 * 1024 * 1024L };
        await forecastService.SaveBudgetAsync(budget);

        var result = await forecastService.GetBudgetResultAsync(10, 5, 2);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBudgetResultAsync_Thresholds_ReturnsCorrectStatus()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var forecastService = new ForecastService(context.Repository, analytics);

        long limit = 100 * 1000 * 1000; // 100 MB
        var budget = new DataBudget
        {
            Enabled = true,
            MonthlyLimitBytes = limit,
            WarningThresholdPct = 80,
            CriticalThresholdPct = 90
        };
        await forecastService.SaveBudgetAsync(budget);

        // Healthy (<80%)
        var r1 = await forecastService.GetBudgetResultAsync(50 * 1000 * 1000, 5 * 1000 * 1000, 5 * 1000 * 1000);
        Assert.NotNull(r1);
        Assert.Equal(BudgetStatus.Healthy, r1.Status);
        Assert.Equal(50, r1.UsedPercent);

        // Warning (80% <= x < 90%)
        var r2 = await forecastService.GetBudgetResultAsync(82 * 1000 * 1000, 5 * 1000 * 1000, 5 * 1000 * 1000);
        Assert.NotNull(r2);
        Assert.Equal(BudgetStatus.Warning, r2.Status);

        // Critical (90% <= x < 100%)
        var r3 = await forecastService.GetBudgetResultAsync(92 * 1000 * 1000, 5 * 1000 * 1000, 5 * 1000 * 1000);
        Assert.NotNull(r3);
        Assert.Equal(BudgetStatus.Critical, r3.Status);

        // Exceeded (>=100%)
        var r4 = await forecastService.GetBudgetResultAsync(105 * 1000 * 1000, 5 * 1000 * 1000, 5 * 1000 * 1000);
        Assert.NotNull(r4);
        Assert.Equal(BudgetStatus.Exceeded, r4.Status);
    }
}
