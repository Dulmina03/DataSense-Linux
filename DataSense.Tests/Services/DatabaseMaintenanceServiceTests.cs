using System;
using System.Threading.Tasks;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class DatabaseMaintenanceServiceTests
{
    [Fact]
    public async Task InspectHealthAsync_ReturnsHealthyState()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var eventService = new EventService();
        var maintenance = new DatabaseMaintenanceService(context.Repository, eventService);

        var health = await maintenance.InspectHealthAsync();

        Assert.NotNull(health);
        Assert.True(health.IsHealthy);
    }

    [Fact]
    public async Task PerformCleanupAsync_PurgesOldRecords()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var eventService = new EventService();
        var maintenance = new DatabaseMaintenanceService(context.Repository, eventService);

        long result = await maintenance.PerformCleanupAsync(TimeSpan.FromDays(30));

        Assert.True(result >= 0);
        Assert.Equal(1, eventService.UnreadCount);
    }

    [Fact]
    public async Task OptimizeDatabaseAsync_ExecutesSuccessfully()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var eventService = new EventService();
        var maintenance = new DatabaseMaintenanceService(context.Repository, eventService);

        bool success = await maintenance.OptimizeDatabaseAsync();

        Assert.True(success);
        Assert.Equal(1, eventService.UnreadCount);
    }
}
