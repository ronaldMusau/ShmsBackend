using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration _configuration;

    public NotificationService(
        ShmsDbContext context,
        IHubContext<NotificationHub> hubContext,
        ILogger<NotificationService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
        _configuration = configuration;
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

        var isPortalUser = IsPortalAudience(audience);
        var group = ResolvePreferenceGroup(category, entityType, isPortalUser);
        var parsedEntityId = string.IsNullOrEmpty(entityId) ? (Guid?)null : Guid.Parse(entityId);

        // Preferences are per-user even for a role broadcast — evaluate each recipient individually.
        var createdNotifications = new List<Notification>();
        var pushRecipients = new List<string>();

        foreach (var userId in userIds)
        {
            if (await ShouldDeliverAsync(userId, isPortalUser, group, "InApp"))
            {
                createdNotifications.Add(new Notification
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
                });
            }

            if (await ShouldDeliverAsync(userId, isPortalUser, group, "Push"))
                pushRecipients.Add(userId);
        }

        if (createdNotifications.Count > 0)
        {
            _context.Notifications.AddRange(createdNotifications);
            await _context.SaveChangesAsync();

            var payload = new { message, category, entityType, entityId = parsedEntityId, createdAt = DateTime.UtcNow };
            foreach (var notification in createdNotifications)
            {
                await _hubContext.Clients.Group($"user_{notification.TargetUserId}").SendAsync("ReceiveNotification", payload);
            }
        }

        foreach (var userId in pushRecipients)
        {
            await SendWebPushAsync(userId, isPortalUser, message);
        }

        _logger.LogInformation("Notification sent to {Count}/{Total} users of role {Audience}: {Message}",
            createdNotifications.Count, userIds.Count, audience, message);
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
        var isPortalUser = Guid.TryParse(userId, out var uid)
            && await _context.PortalUsers.AsNoTracking().AnyAsync(u => u.Id == uid);

        var group = ResolvePreferenceGroup(category, entityType, isPortalUser);

        var deliverInApp = await ShouldDeliverAsync(userId, isPortalUser, group, "InApp");
        var deliverPush = await ShouldDeliverAsync(userId, isPortalUser, group, "Push");

        // Both channels muted for this category — nothing to do.
        if (!deliverInApp && !deliverPush)
            return;

        if (deliverInApp)
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
        }

        if (deliverPush)
            await SendWebPushAsync(userId, isPortalUser, message);

        _logger.LogInformation("Notification sent to user {UserId}: {Message}", userId, message);
    }

    // ── Push / preference enforcement ────────────────────────────────────────

    private static bool IsPortalAudience(NotificationAudience audience) => audience switch
    {
        NotificationAudience.Landlord or NotificationAudience.Agent
            or NotificationAudience.Tenant or NotificationAudience.Explorer => true,
        _ => false
    };

    // Maps a notification's (category, entityType) to a NotificationPreference group name.
    private string ResolvePreferenceGroup(string category, string? entityType, bool isPortalUser)
    {
        if (entityType == "Complaint")
            return "Complaints";

        if (entityType is "FlatEdit" or "Flat" or "Vacate")
            return "Properties";

        if (entityType == "Agreement")
            return "Account";

        // No entity reference — fall back to the category.
        return category switch
        {
            "payment" => "Rent",
            "security" => "Account",
            "user" => isPortalUser ? "Account" : "TeamActivity",
            _ => "Account"
        };
    }

    // Returns whether a given channel ("InApp" / "Push" / "Email") is enabled for this user
    // and group. No preference row => opt-out model default of all-true (matches GetOrCreateAsync).
    private async Task<bool> ShouldDeliverAsync(string userId, bool isPortalUser, string group, string channel)
    {
        _logger.LogInformation("PUSH-DEBUG: Checking delivery for user {UserId}, group {Group}, channel {Channel}", userId, group, channel);

        if (!Guid.TryParse(userId, out var uid))
            return true;

        var pref = await _context.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == uid && p.IsPortalUser == isPortalUser);

        if (pref == null)
            return true;

        bool Flag(string name) =>
            (bool?)typeof(NotificationPreference).GetProperty($"{name}{channel}Enabled")?.GetValue(pref) ?? true;

        return Flag("Master") && Flag(group);
    }

    private async Task SendWebPushAsync(string userId, bool isPortalUser, string message)
    {
        if (!Guid.TryParse(userId, out var uid))
            return;

        var publicKey = _configuration["WebPush:VapidPublicKey"];
        var privateKey = _configuration["WebPush:VapidPrivateKey"];
        var subject = _configuration["WebPush:VapidSubject"];

        if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey) || string.IsNullOrEmpty(subject))
        {
            _logger.LogWarning("WebPush VAPID configuration missing; skipping web push for user {UserId}", userId);
            return;
        }

        var subscriptions = await _context.PushSubscriptions
            .Where(s => s.UserId == uid && s.IsPortalUser == isPortalUser)
            .ToListAsync();

        _logger.LogInformation("PUSH-DEBUG: Found {Count} push subscription(s) for user {UserId} (isPortalUser={IsPortalUser})", subscriptions.Count, userId, isPortalUser);

        if (subscriptions.Count == 0)
            return;

        var vapidDetails = new WebPush.VapidDetails(subject, publicKey, privateKey);
        using var client = new WebPush.WebPushClient();
        var payload = JsonSerializer.Serialize(new { title = "Romah Estates", body = message });

        var stale = new List<PushSubscription>();

        foreach (var sub in subscriptions)
        {
            try
            {
                _logger.LogInformation("PUSH-DEBUG: Attempting to send push to subscription {SubId}, endpoint starting {EndpointPrefix}", sub.Id, sub.Endpoint.Substring(0, Math.Min(50, sub.Endpoint.Length)));
                var target = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await client.SendNotificationAsync(target, payload, vapidDetails);
                _logger.LogInformation("PUSH-DEBUG: Successfully sent push to subscription {SubId}", sub.Id);
            }
            catch (WebPush.WebPushException ex)
                when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.Gone)
            {
                _logger.LogInformation(
                    "Push subscription {SubscriptionId} for user {UserId} is gone ({Status}); removing it.",
                    sub.Id, userId, (int)ex.StatusCode);
                stale.Add(sub);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to deliver web push to subscription {SubscriptionId} for user {UserId}", sub.Id, userId);
            }
        }

        if (stale.Count > 0)
        {
            _context.PushSubscriptions.RemoveRange(stale);
            await _context.SaveChangesAsync();
        }
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
