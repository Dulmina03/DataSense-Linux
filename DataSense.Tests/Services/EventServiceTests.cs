using System;
using System.Linq;
using DataSense.Models;
using DataSense.Services;
using Xunit;

namespace DataSense.Tests.Services;

public class EventServiceTests
{
    [Fact]
    public void PublishEvent_IncrementsUnreadCountAndRetrievesActive()
    {
        var service = new EventService();

        service.PublishEvent(new DataSenseEvent
        {
            Title = "Budget Warning",
            Severity = EventSeverity.Warning,
            EventType = DataSenseEventType.BudgetWarning
        });

        Assert.Equal(1, service.UnreadCount);
        Assert.Single(service.GetActiveEvents());
    }

    [Fact]
    public void Deduplication_IgnoresDuplicateFingerprintWithinCooldown()
    {
        var service = new EventService();
        string fingerprint = "budget_warning_80";

        service.PublishEvent(new DataSenseEvent
        {
            Title = "Warning 1",
            Fingerprint = fingerprint
        });

        service.PublishEvent(new DataSenseEvent
        {
            Title = "Warning 2",
            Fingerprint = fingerprint
        });

        Assert.Equal(1, service.UnreadCount);
        var events = service.GetActiveEvents();
        Assert.Single(events);
        Assert.Equal("Warning 1", events[0].Title);
    }

    [Fact]
    public void MarkAsRead_UpdatesUnreadCount()
    {
        var service = new EventService();
        var evt = new DataSenseEvent { Title = "Test Event" };
        service.PublishEvent(evt);

        Assert.Equal(1, service.UnreadCount);

        service.MarkAsRead(evt.Id);
        Assert.Equal(0, service.UnreadCount);
    }

    [Fact]
    public void MaxLimit_CapsAt100Events()
    {
        var service = new EventService();

        for (int i = 0; i < 110; i++)
        {
            service.PublishEvent(new DataSenseEvent
            {
                Title = $"Event {i}",
                Fingerprint = $"fp_{i}"
            });
        }

        Assert.True(service.GetActiveEvents().Count <= 100);
    }
}
