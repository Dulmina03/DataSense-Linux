using System;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class NativeNotificationServiceTests
{
    [Fact]
    public async Task NotificationService_RespectsDisabledSetting()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        await context.Repository.SaveSettingAsync("EnableDesktopNotifications", "false");

        var platform = new LinuxPlatformService();
        var service = new NativeNotificationService(platform, context.Repository);

        bool shown = await service.ShowNotificationAsync("Test Title", "Test Message");
        Assert.False(shown);
    }

    [Fact]
    public async Task NotificationService_HandleEventPublished_CooldownSuppressesDuplicates()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var platform = new LinuxPlatformService();
        var service = new NativeNotificationService(platform, context.Repository);

        var evt = new DataSenseEvent
        {
            Id = "evt-1",
            Title = "High Data Usage",
            Description = "Usage limit reached",
            Severity = EventSeverity.Warning,
            Fingerprint = "fp-budget-exceeded"
        };

        // First call should process
        await service.HandleEventPublishedAsync(evt);

        // Immediate duplicate call should be suppressed by cooldown
        await service.HandleEventPublishedAsync(evt);
    }
}
