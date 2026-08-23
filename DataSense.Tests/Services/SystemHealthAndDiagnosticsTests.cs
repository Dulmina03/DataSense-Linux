using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class SystemHealthAndDiagnosticsTests
{
    [Fact]
    public void SystemHealthRegistry_DefaultState_IsOptimal()
    {
        var registry = new SystemHealthRegistry();

        Assert.NotNull(registry.GetAllReports());
        Assert.True(registry.GetAllReports().Count >= 5);
        Assert.Equal(DataSense.Models.DataSenseHealthStatus.Optimal, registry.OverallHealth);
    }

    [Fact]
    public void SystemHealthRegistry_SubsystemError_UpdatesOverallHealth()
    {
        var registry = new SystemHealthRegistry();

        registry.ReportHealth("SQLiteDatabase", SubsystemState.Error, "Disk error");

        Assert.Equal(DataSense.Models.DataSenseHealthStatus.Degraded, registry.OverallHealth);
        var report = registry.GetReport("SQLiteDatabase");
        Assert.Equal(SubsystemState.Error, report.State);
        Assert.Equal("Disk error", report.Message);
    }

    [Fact]
    public async Task DiagnosticsService_GetDiagnosticsAsync_ReturnsAllComponents()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var registry = new SystemHealthRegistry();
        var diagService = new DiagnosticsService(registry, context.Repository);

        var components = (await diagService.GetDiagnosticsAsync()).ToList();

        Assert.Equal(8, components.Count);
        Assert.Contains(components, c => c.Name == "SQLiteDatabase" && c.Status == SubsystemState.Healthy);
    }
}
