using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Notifications;

public interface INotificationService
{
    // Send to all users of a specific role (e.g. all SuperAdmins)
    Task SendToRoleAsync(NotificationAudience audience, string message, string category = "general",
        string? entityType = null, string? entityId = null);

    // Send to multiple roles at once
    Task SendToRolesAsync(IEnumerable<NotificationAudience> audiences, string message, string category = "general",
        string? entityType = null, string? entityId = null);

    // Send to one specific user by their ID
    Task SendToUserAsync(string userId, string message, string category = "general",
        string? entityType = null, string? entityId = null);

    // Send to one specific user, bypassing their In-App/Push preference toggles entirely
    // (mirrors the always-on email pattern — for alerts that must never be silently muted).
    Task SendForcedToUserAsync(string userId, string message, string category = "general",
        string? entityType = null, string? entityId = null);

    // Fetch all notifications for a specific user (by role + specific)
    Task<IEnumerable<Notification>> GetForUserAsync(string userId, NotificationAudience? userRole);

    // Mark a single notification as read
    Task<bool> MarkAsReadAsync(Guid notificationId, string userId);

    // Mark all notifications as read for a user
    Task MarkAllAsReadAsync(string userId, NotificationAudience? userRole);

    // Mark a set of notifications as read (ownership-scoped to the user)
    Task MarkBulkAsReadAsync(string userId, IEnumerable<Guid> notificationIds);

    // Delete a specific notification
    Task<bool> DeleteAsync(Guid notificationId, string userId);

    // Delete a set of notifications (ownership-scoped to the user)
    Task DeleteBulkAsync(string userId, IEnumerable<Guid> notificationIds);

    // Delete all notifications for a user
    Task DeleteAllAsync(string userId, NotificationAudience? userRole);

    // Count unread notifications for a user
    Task<int> GetUnreadCountAsync(string userId, NotificationAudience? userRole);
}
