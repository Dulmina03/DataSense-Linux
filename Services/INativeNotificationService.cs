using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public enum NotificationUrgency
{
    Low,
    Normal,
    Critical
}

public interface INativeNotificationService
{
    Task<bool> ShowNotificationAsync(string title, string message, NotificationUrgency urgency = NotificationUrgency.Normal, string? category = null);
    Task HandleEventPublishedAsync(DataSenseEvent evt);
}
