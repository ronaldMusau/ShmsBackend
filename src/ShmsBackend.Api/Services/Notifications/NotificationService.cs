using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Hubs;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly ShmsDbContext _context;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ShmsDbContext context,
        IHubContext<NotificationHub> hubContext,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendToRoleAsync(NotificationAudience audience, string message, string category = "general")
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            Audience = audience,
            TargetUserId = null,
            Message = message,
            Category = category,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        await _hubContext.Clients.Group($"role_{audience}").SendAsync("ReceiveNotification", new
        {
            id = notification.Id,
            message = notification.Message,
            category = notification.Category,
            isRead = false,
            createdAt = notification.CreatedAt
        });

        _logger.LogInformation("Notification sent to role {Audience}: {Message}", audience, message);
    }

    public async Task SendToRolesAsync(IEnumerable<NotificationAudience> audiences, string message, string category = "general")
    {
        foreach (var audience in audiences)
        {
            await SendToRoleAsync(audience, message, category);
        }
    }

    public async Task SendToUserAsync(string userId, string message, string category = "general")
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            Audience = NotificationAudience.SpecificUser,
            TargetUserId = userId,
            Message = message,
            Category = category,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        await _hubContext.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", new
        {
            id = notification.Id,
            message = notification.Message,
            category = notification.Category,
            isRead = false,
            createdAt = notification.CreatedAt
        });

        _logger.LogInformation("Notification sent to user {UserId}: {Message}", userId, message);
    }

    public async Task<IEnumerable<Notification>> GetForUserAsync(string userId, NotificationAudience? userRole)
    {
        var query = _context.Notifications.AsQueryable();

        if (userRole.HasValue)
        {
            query = query.Where(n =>
                (n.Audience == userRole.Value && n.TargetUserId == null) ||
                (n.Audience == NotificationAudience.SpecificUser && n.TargetUserId == userId)
            );
        }
        else
        {
            query = query.Where(n => n.TargetUserId == userId);
        }

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync();
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, string userId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId &&
                (n.TargetUserId == userId || n.Audience != NotificationAudience.SpecificUser));

        if (notification == null) return false;

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task MarkAllAsReadAsync(string userId, NotificationAudience? userRole)
    {
        var notifications = await _context.Notifications
            .Where(n => !n.IsRead &&
                ((userRole.HasValue && n.Audience == userRole.Value && n.TargetUserId == null) ||
                 (n.Audience == NotificationAudience.SpecificUser && n.TargetUserId == userId)))
            .ToListAsync();

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(Guid notificationId, string userId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId &&
                (n.TargetUserId == userId || n.Audience != NotificationAudience.SpecificUser));

        if (notification == null) return false;

        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task DeleteAllAsync(string userId, NotificationAudience? userRole)
    {
        var notifications = await _context.Notifications
            .Where(n =>
                (userRole.HasValue && n.Audience == userRole.Value && n.TargetUserId == null) ||
                (n.Audience == NotificationAudience.SpecificUser && n.TargetUserId == userId))
            .ToListAsync();

        _context.Notifications.RemoveRange(notifications);
        await _context.SaveChangesAsync();
    }

    public async Task<int> GetUnreadCountAsync(string userId, NotificationAudience? userRole)
    {
        return await _context.Notifications
            .CountAsync(n => !n.IsRead &&
                ((userRole.HasValue && n.Audience == userRole.Value && n.TargetUserId == null) ||
                 (n.Audience == NotificationAudience.SpecificUser && n.TargetUserId == userId)));
    }
}
