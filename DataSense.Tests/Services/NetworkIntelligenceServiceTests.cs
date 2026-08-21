using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class NetworkIntelligenceServiceTests
{
    [Fact]
    public async Task GetNetworkProfilesAsync_ReturnsProfilesForKnownNetworks()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var connService = new MockNetworkConnectionService();
        var intelService = new NetworkIntelligenceService(analytics, connService);

        DateTime now = DateTime.UtcNow;
        await TestDataBuilder.SeedSessionAsync(context.Repository, "HomeNet", "wlan0", now.AddHours(-5), TimeSpan.FromHours(1), 1000000, 200000);

        var profiles = await intelService.GetNetworkProfilesAsync();

        Assert.NotEmpty(profiles);
        Assert.Contains(profiles, p => p.NetworkName == "HomeNet");
    }

    [Fact]
    public async Task GetNetworkPerformanceProfilesAsync_WithSpeedTests_ReturnsProfiles()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var connService = new MockNetworkConnectionService();
        var intelService = new NetworkIntelligenceService(analytics, connService);

        DateTime now = DateTime.UtcNow;
        await TestDataBuilder.SeedSessionAsync(context.Repository, "HomeNet", "wlan0", now.AddHours(-2), TimeSpan.FromHours(1), 500000, 100000);
        await TestDataBuilder.SeedSpeedTestAsync(context.Repository, "HomeNet", 100.0, 50.0, 10.0, now);

        var perf = await intelService.GetNetworkPerformanceProfilesAsync();

        Assert.NotNull(perf);
        Assert.NotEmpty(perf);
        var homeNetPerf = perf.FirstOrDefault(p => p.NetworkName == "HomeNet");
        Assert.NotNull(homeNetPerf);
        Assert.True(homeNetPerf.AverageDownloadSpeed > 0);
        Assert.True(homeNetPerf.PerformanceScore >= 0);
    }
}
