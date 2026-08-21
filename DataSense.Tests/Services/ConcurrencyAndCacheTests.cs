using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class ConcurrencyAndCacheTests
{
    [Fact]
    public async Task SqliteRepository_ConcurrentWrites_SucceedWithoutDatabaseLocks()
    {
        using var context = await TestDatabaseFactory.CreateAsync();

        DateTime now = DateTime.UtcNow;
        var tasks = Enumerable.Range(0, 20).Select(i =>
            context.Repository.SaveUsageAsync(new NetworkUsage
            {
                Timestamp = now.AddSeconds(i),
                InterfaceName = "wlan0",
                DownloadSpeed = 10,
                UploadSpeed = 5,
                BytesReceived = 1000 + (i * 100),
                BytesSent = 500 + (i * 50)
            })
        ).ToArray();

        await Task.WhenAll(tasks);

        var (rx, tx) = await context.Repository.GetTodaySummaryAsync("wlan0");
        Assert.True(rx > 0);
    }

    [Fact]
    public void EventService_ParallelPublishes_IsThreadSafe()
    {
        var eventService = new EventService();

        Parallel.For(0, 50, i =>
        {
            eventService.PublishEvent(new DataSenseEvent
            {
                Title = $"Parallel Event {i}",
                Fingerprint = $"fp_{i}"
            });
        });

        Assert.Equal(50, eventService.GetActiveEvents().Count);
    }
}
