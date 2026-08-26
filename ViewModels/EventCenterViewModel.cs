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

    public bool IsFilterAll => FilterSeverity.Equals("All", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterCritical => FilterSeverity.Equals("Critical", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterWarning => FilterSeverity.Equals("Warning", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterInfo => FilterSeverity.Equals("Info", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterSuccess => FilterSeverity.Equals("Success", StringComparison.OrdinalIgnoreCase);

    partial void OnFilterSeverityChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterCritical));
        OnPropertyChanged(nameof(IsFilterWarning));
        OnPropertyChanged(nameof(IsFilterInfo));
        OnPropertyChanged(nameof(IsFilterSuccess));
    }

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

        var activeIds = active.Select(x => x.Id).ToHashSet();
        for (int i = Events.Count - 1; i >= 0; i--)
        {
            if (!activeIds.Contains(Events[i].Id))
            {
                Events.RemoveAt(i);
            }
        }

        for (int i = 0; i < active.Count; i++)
        {
            var item = active[i];
            int existingIndex = -1;
            for (int j = 0; j < Events.Count; j++)
            {
                if (Events[j].Id == item.Id)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                if (existingIndex != i && i < Events.Count)
                {
                    Events.Move(existingIndex, i);
                }
            }
            else
            {
                Events.Insert(Math.Min(i, Events.Count), item);
            }
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
