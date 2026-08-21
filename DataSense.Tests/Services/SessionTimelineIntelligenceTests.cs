using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;

namespace DataSense.Tests.Services;

public class SessionTimelineIntelligenceTests
{
    [Fact]
    public async Task GetTimelineAsync_ReturnsSessionsInChronologicalOrder()
    {
        // Arrange
        using var context = await TestDatabaseFactory.CreateAsync();
        var mockEventService = new EventService(null);
        var sessionManager = new NetworkSessionManager(null!, null!, context.Repository);
        var mockPatternService = new Mock<IPatternAnalysisService>();
        var service = new SessionIntelligenceService(context.Repository, sessionManager, mockPatternService.Object, mockEventService);

        var now = DateTime.UtcNow;

        await context.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Session1",
            StartTime = now.AddHours(-3),
            EndTime = now.AddHours(-2),
            BytesDownloaded = 1000,
            BytesUploaded = 500
        });

        await context.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Session2",
            StartTime = now.AddHours(-1),
            EndTime = now,
            BytesDownloaded = 2000,
            BytesUploaded = 1000
        });

        // Act
        var timeline = await service.GetSessionTimelineAsync(now.AddDays(-1), now.AddDays(1));

        // Assert
        Assert.Equal(2, timeline.Count());
        Assert.Equal("Session2", timeline.First().Session.NetworkName); // descending by default
        Assert.Equal("Session1", timeline.Last().Session.NetworkName);
    }

    [Fact]
    public async Task DetectNetworkSwitches_IdentifiesValidSwitches()
    {
        // Arrange
        using var context = await TestDatabaseFactory.CreateAsync();
        var mockEventService = new EventService(null);
        var sessionManager = new NetworkSessionManager(null!, null!, context.Repository);
        var mockPatternService = new Mock<IPatternAnalysisService>();
        var service = new SessionIntelligenceService(context.Repository, sessionManager, mockPatternService.Object, mockEventService);

        var now = DateTime.UtcNow;

        await context.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Wi-Fi Network",
            ConnectionType = "802.11 Wi-Fi",
            StartTime = now.AddMinutes(-30),
            EndTime = now.AddMinutes(-10),
            BytesDownloaded = 1000,
            BytesUploaded = 500
        });

        await context.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Wired Network",
            ConnectionType = "802.3 Ethernet",
            StartTime = now.AddMinutes(-5),
            EndTime = now,
            BytesDownloaded = 2000,
            BytesUploaded = 1000
        });

        // Act
        var switches = await service.GetNetworkSwitchTimelineAsync(now.AddDays(-1), now.AddDays(1));

        // Assert
        Assert.Single(switches);
        Assert.Equal("Wi-Fi Network", switches.First().OldNetwork);
        Assert.Equal("Wired Network", switches.First().NewNetwork);
    }
}
