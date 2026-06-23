using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Notifications;

public interface INotificationService
{
    // Send to all users of a specific role (e.g. all SuperAdmins)
    Task SendToRoleAsync(NotificationAudience audience, string message, string category = "general");

    // Send to multiple roles at once
    Task SendToRolesAsync(IEnumerable<NotificationAudience> audiences, string message, string category = "general");

    // Send to one specific user by their ID
    Task SendToUserAsync(string userId, string message, string category = "general");

    // Fetch all notifications for a specific user (by role + specific)
    Task<IEnumerable<Notification>> GetForUserAsync(string userId, NotificationAudience? userRole);

    // Mark a single notification as read
    Task<bool> MarkAsReadAsync(Guid notificationId, string userId);

    // Mark all notifications as read for a user
    Task MarkAllAsReadAsync(string userId, NotificationAudience? userRole);

    // Delete a specific notification
    Task<bool> DeleteAsync(Guid notificationId, string userId);

    // Delete all notifications for a user
    Task DeleteAllAsync(string userId, NotificationAudience? userRole);

    // Count unread notifications for a user
    Task<int> GetUnreadCountAsync(string userId, NotificationAudience? userRole);
}
