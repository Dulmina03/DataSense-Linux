using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class EventCenterViewModel : ViewModelBase
{
    private readonly IEventService _eventService;

    public override string Title => "Event Center";

    [ObservableProperty] private int    _unreadCount = 0;
    [ObservableProperty] private string _filterSeverity = "All";

    public ObservableCollection<DataSenseEvent> Events { get; } = new();

    public EventCenterViewModel(IEventService eventService)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _eventService.EventsUpdated += OnEventsUpdated;
        RefreshEvents();
    }

    private void OnEventsUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshEvents);
    }

    private void RefreshEvents()
    {
        UnreadCount = _eventService.UnreadCount;
        var active  = _eventService.GetActiveEvents();

        if (FilterSeverity != "All")
        {
            active = active.Where(e => e.Severity.ToString().Equals(FilterSeverity, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Events.Clear();
        foreach (var evt in active)
        {
            Events.Add(evt);
        }
    }

    [RelayCommand]
    private void MarkAllRead()
    {
        _eventService.MarkAllAsRead();
    }

    [RelayCommand]
    private void ClearResolved()
    {
        _eventService.ClearResolvedEvents();
    }

    [RelayCommand]
    private void Dismiss(string id)
    {
        if (!string.IsNullOrEmpty(id))
            _eventService.DismissEvent(id);
    }

    [RelayCommand]
    private void MarkRead(string id)
    {
        if (!string.IsNullOrEmpty(id))
            _eventService.MarkAsRead(id);
    }

    [RelayCommand]
    private void SetFilter(string severity)
    {
        FilterSeverity = severity;
        RefreshEvents();
    }
}
