using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IEventService
{
    IReadOnlyList<DataSenseEvent> GetActiveEvents();
    int UnreadCount { get; }
    void PublishEvent(DataSenseEvent evt);
    void MarkAsRead(string eventId);
    void MarkAllAsRead();
    void DismissEvent(string eventId);
    void ClearResolvedEvents();
    event EventHandler? EventsUpdated;
}

public class EventService : IEventService
{
    private readonly ConcurrentDictionary<string, DataSenseEvent> _events = new();
    private readonly ConcurrentDictionary<string, DateTime> _fingerprintCooldowns = new();
    private readonly TimeSpan _cooldownDuration = TimeSpan.FromMinutes(15);
    private readonly INativeNotificationService? _notificationService;

    public event EventHandler? EventsUpdated;

    public int UnreadCount => _events.Values.Count(e => !e.IsRead && !e.IsDismissed);

    public EventService(INativeNotificationService? notificationService = null)
    {
        _notificationService = notificationService;
    }

    public IReadOnlyList<DataSenseEvent> GetActiveEvents()
    {
        return _events.Values
            .Where(e => !e.IsDismissed)
            .OrderByDescending(e => e.Severity)
            .ThenByDescending(e => e.Timestamp)
            .ToList();
    }

    public void PublishEvent(DataSenseEvent evt)
    {
        if (string.IsNullOrEmpty(evt.Fingerprint))
        {
            _events[evt.Id] = evt;
            EventsUpdated?.Invoke(this, EventArgs.Empty);
            TriggerNotification(evt);
            return;
        }

        // Deduplication using fingerprint cooldown
        var now = DateTime.UtcNow;
        if (_fingerprintCooldowns.TryGetValue(evt.Fingerprint, out var lastTime))
        {
            if (now - lastTime < _cooldownDuration) return; // Ignore duplicate within cooldown
        }

        _fingerprintCooldowns[evt.Fingerprint] = now;
        _events[evt.Id] = evt;

        // Keep maximum 100 recent events in memory
        if (_events.Count > 100)
        {
            var oldestKey = _events.Values.OrderBy(e => e.Timestamp).FirstOrDefault()?.Id;
            if (oldestKey != null) _events.TryRemove(oldestKey, out _);
        }

        EventsUpdated?.Invoke(this, EventArgs.Empty);
        TriggerNotification(evt);
    }

    public void MarkAsRead(string eventId)
    {
        if (_events.TryGetValue(eventId, out var evt))
        {
            evt.IsRead = true;
            EventsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MarkAllAsRead()
    {
        foreach (var evt in _events.Values)
        {
            evt.IsRead = true;
        }
        EventsUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void DismissEvent(string eventId)
    {
        if (_events.TryGetValue(eventId, out var evt))
        {
            evt.IsDismissed = true;
            EventsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearResolvedEvents()
    {
        var resolved = _events.Values.Where(e => e.IsRead || e.IsDismissed || e.Severity == EventSeverity.Success).Select(e => e.Id).ToList();
        foreach (var id in resolved)
        {
            _events.TryRemove(id, out _);
        }
        EventsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void TriggerNotification(DataSenseEvent evt)
    {
        if (_notificationService != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _notificationService.HandleEventPublishedAsync(evt);
                }
                catch { /* Best effort notification dispatch */ }
            });
        }
    }
}
