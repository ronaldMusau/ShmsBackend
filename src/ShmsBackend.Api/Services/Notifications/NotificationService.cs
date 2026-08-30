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

    public async Task SendToRoleAsync(NotificationAudience audience, string message, string category = "general",
        string? entityType = null, string? entityId = null)
    {
        var userIds = await GetUserIdsByAudienceAsync(audience);

        if (userIds.Count == 0)
        {
            _logger.LogInformation("No users found for role {Audience}, skipping notification", audience);
            return;
        }

        var parsedEntityId = string.IsNullOrEmpty(entityId) ? (Guid?)null : Guid.Parse(entityId);

        var notifications = userIds.Select(userId => new Notification
        {
            Id = Guid.NewGuid(),
            Audience = NotificationAudience.SpecificUser,
            TargetUserId = userId,
            Message = message,
            Category = category,
            EntityType = entityType,
            EntityId = parsedEntityId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        _context.Notifications.AddRange(notifications);
        await _context.SaveChangesAsync();

        var payload = new { message, category, entityType, entityId = parsedEntityId, createdAt = DateTime.UtcNow };
        foreach (var userId in userIds)
        {
            await _hubContext.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", payload);
        }

        _logger.LogInformation("Notification sent to {Count} users of role {Audience}: {Message}",
            userIds.Count, audience, message);
    }

    public async Task SendToRolesAsync(IEnumerable<NotificationAudience> audiences, string message, string category = "general",
        string? entityType = null, string? entityId = null)
    {
        foreach (var audience in audiences)
        {
            await SendToRoleAsync(audience, message, category, entityType, entityId);
        }
    }

    public async Task SendToUserAsync(string userId, string message, string category = "general",
        string? entityType = null, string? entityId = null)
    {
        var parsedEntityId = string.IsNullOrEmpty(entityId) ? (Guid?)null : Guid.Parse(entityId);

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            Audience = NotificationAudience.SpecificUser,
            TargetUserId = userId,
            Message = message,
            Category = category,
            EntityType = entityType,
            EntityId = parsedEntityId,
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
            entityType = notification.EntityType,
            entityId = notification.EntityId,
            isRead = false,
            createdAt = notification.CreatedAt
        });

        _logger.LogInformation("Notification sent to user {UserId}: {Message}", userId, message);
    }

    public async Task<IEnumerable<Notification>> GetForUserAsync(string userId, NotificationAudience? userRole)
    {
        return await _context.Notifications
            .Where(n => n.Audience == NotificationAudience.SpecificUser && n.TargetUserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync();
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, string userId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.TargetUserId == userId);

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
                n.Audience == NotificationAudience.SpecificUser &&
                n.TargetUserId == userId)
            .ToListAsync();

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();
    }

    public async Task MarkBulkAsReadAsync(string userId, IEnumerable<Guid> notificationIds)
    {
        var ids = notificationIds?.Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0) return;

        var notifications = await _context.Notifications
            .Where(n => ids.Contains(n.Id) && n.TargetUserId == userId)
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
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.TargetUserId == userId);

        if (notification == null) return false;

        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task DeleteBulkAsync(string userId, IEnumerable<Guid> notificationIds)
    {
        var ids = notificationIds?.Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0) return;

        var notifications = await _context.Notifications
            .Where(n => ids.Contains(n.Id) && n.TargetUserId == userId)
            .ToListAsync();

        _context.Notifications.RemoveRange(notifications);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAllAsync(string userId, NotificationAudience? userRole)
    {
        var notifications = await _context.Notifications
            .Where(n => n.Audience == NotificationAudience.SpecificUser && n.TargetUserId == userId)
            .ToListAsync();

        _context.Notifications.RemoveRange(notifications);
        await _context.SaveChangesAsync();
    }

    public async Task<int> GetUnreadCountAsync(string userId, NotificationAudience? userRole)
    {
        return await _context.Notifications
            .CountAsync(n => !n.IsRead &&
                n.Audience == NotificationAudience.SpecificUser &&
                n.TargetUserId == userId);
    }

    private async Task<List<string>> GetUserIdsByAudienceAsync(NotificationAudience audience)
    {
        return audience switch
        {
            NotificationAudience.SuperAdmin =>
                (await _context.SuperAdmins.Select(u => u.Id).ToListAsync()).ConvertAll(id => id.ToString()),
            NotificationAudience.Admin =>
                (await _context.AdminUsers.Select(u => u.Id).ToListAsync()).ConvertAll(id => id.ToString()),
            NotificationAudience.Manager =>
                (await _context.Managers.Select(u => u.Id).ToListAsync()).ConvertAll(id => id.ToString()),
            NotificationAudience.Accountant =>
                (await _context.Accountants.Select(u => u.Id).ToListAsync()).ConvertAll(id => id.ToString()),
            NotificationAudience.Secretary =>
                (await _context.Secretaries.Select(u => u.Id).ToListAsync()).ConvertAll(id => id.ToString()),
            NotificationAudience.Landlord =>
                (await _context.Landlords.Select(u => u.Id).ToListAsync()).ConvertAll(id => id.ToString()),
            NotificationAudience.Agent =>
                (await _context.Agents.Select(u => u.Id).ToListAsync()).ConvertAll(id => id.ToString()),
            NotificationAudience.Tenant =>
                (await _context.Tenants.Select(u => u.Id).ToListAsync()).ConvertAll(id => id.ToString()),
            NotificationAudience.Explorer =>
                (await _context.Explorers.Select(u => u.Id).ToListAsync()).ConvertAll(id => id.ToString()),
            _ => new List<string>()
        };
    }
}
