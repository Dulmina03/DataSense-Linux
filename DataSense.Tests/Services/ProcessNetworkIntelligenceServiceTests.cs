using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Moq;
using Xunit;

namespace DataSense.Tests.Services;

public class ProcessNetworkIntelligenceServiceTests
{
    [Fact]
    public async Task GetNetworkProcessUsageAsync_WithOverlappingSession_ReturnsRankedUsage()
    {
        // Arrange
        using var context = await TestDatabaseFactory.CreateAsync();
        var mockResolver = new Mock<ILinuxProcessResolver>();
        var service = new ProcessNetworkIntelligenceService(context.Repository, mockResolver.Object);

        DateTime now = DateTime.UtcNow;

        // Seed session on "HomeWiFi"
        await TestDataBuilder.SeedSessionAsync(
            context.Repository,
            "HomeWiFi",
            "wlan0",
            now.AddMinutes(-30),
            TimeSpan.FromMinutes(60),
            10_000_000,
            2_000_000
        );

        // Seed process usage during that session
        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "chrome", now, 8_000_000, 1_000_000);
        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "firefox", now, 1_000_000, 500_000);

        // Act
        var chromeProfiles = (await service.GetProcessNetworkUsageAsync("chrome", 0, 0)).ToList();
        var rankedUsage = (await service.GetNetworkProcessUsageAsync("HomeWiFi")).ToList();

        // Assert
        Assert.Single(chromeProfiles);
        Assert.Equal("HomeWiFi", chromeProfiles[0].NetworkName);
        Assert.Equal(9_000_000, chromeProfiles[0].TotalBytes);

        Assert.Equal(2, rankedUsage.Count);
        Assert.Equal("chrome", rankedUsage[0].ProcessName);
        Assert.Equal(1, rankedUsage[0].Rank);
        Assert.Equal("firefox", rankedUsage[1].ProcessName);
        Assert.Equal(2, rankedUsage[1].Rank);
    }

    [Fact]
    public async Task GetNetworkSpecificBehaviorInsightsAsync_IdentifiesDownloadAndUploadHeavyApps()
    {
        // Arrange
        using var context = await TestDatabaseFactory.CreateAsync();
        var mockResolver = new Mock<ILinuxProcessResolver>();
        var service = new ProcessNetworkIntelligenceService(context.Repository, mockResolver.Object);

        DateTime now = DateTime.UtcNow;

        // Seed session on "OfficeWiFi"
        await TestDataBuilder.SeedSessionAsync(
            context.Repository,
            "OfficeWiFi",
            "wlan0",
            now.AddMinutes(-30),
            TimeSpan.FromMinutes(60),
            50_000_000,
            50_000_000
        );

        // chrome is download heavy (90% download, > 10MB total)
        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "chrome", now, 18_000_000, 2_000_000);
        // git is upload heavy (90% upload, > 10MB total)
        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "git", now, 2_000_000, 18_000_000);

        // Act
        var insights = (await service.GetNetworkSpecificBehaviorInsightsAsync()).ToList();

        // Assert
        Assert.NotEmpty(insights);
        
        var dlHeavy = insights.FirstOrDefault(i => i.Description.Contains("download-heavy"));
        var ulHeavy = insights.FirstOrDefault(i => i.Description.Contains("upload-heavy"));

        Assert.NotNull(dlHeavy);
        Assert.Contains("chrome", dlHeavy.Description);

        Assert.NotNull(ulHeavy);
        Assert.Contains("git", ulHeavy.Description);
    }

    [Fact]
    public async Task GetProcessNetworkAnomaliesAsync_DetectsZScoreSpikes()
    {
        // Arrange
        using var context = await TestDatabaseFactory.CreateAsync();
        var mockResolver = new Mock<ILinuxProcessResolver>();
        var service = new ProcessNetworkIntelligenceService(context.Repository, mockResolver.Object);

        DateTime now = DateTime.UtcNow;

        // Seed multiple historical points to establish standard baseline (average ~100KB)
        for (int i = 10; i >= 1; i--)
        {
            DateTime time = now.AddDays(-i);
            await TestDataBuilder.SeedSessionAsync(
                context.Repository,
                "HomeNet",
                "wlan0",
                time.AddMinutes(-5),
                TimeSpan.FromMinutes(10),
                200_000,
                50_000
            );
            await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "curl", time, 100_000, 20_000);
        }

        // Now seed an anomaly (huge spike of 50MB)
        await TestDataBuilder.SeedSessionAsync(
            context.Repository,
            "HomeNet",
            "wlan0",
            now.AddMinutes(-5),
            TimeSpan.FromMinutes(10),
            100_000_000,
            20_000_000
        );
        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "curl", now, 50_000_000, 10_000_000);

        // Act
        var anomalies = (await service.GetProcessNetworkAnomaliesAsync()).ToList();

        // Assert
        Assert.NotEmpty(anomalies);
        var curlAnomaly = anomalies.FirstOrDefault(a => a.ProcessName == "curl");
        Assert.NotNull(curlAnomaly);
        Assert.Contains("above baseline", curlAnomaly.Description);
        Assert.True(curlAnomaly.DeviationSigma > 3.0);
    }
}
