using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private string GetUserId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

    private NotificationAudience? GetUserRole()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(role)) return null;
        return Enum.TryParse<NotificationAudience>(role, out var audience) ? audience : (NotificationAudience?)null;
    }

    // GET /api/notifications
    [HttpGet]
    public async Task<IActionResult> GetMyNotifications()
    {
        var userId = GetUserId();
        var notifications = await _notificationService.GetForUserAsync(userId, GetUserRole());
        return Ok(notifications);
    }

    // GET /api/notifications/unread-count
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = GetUserId();
        var count = await _notificationService.GetUnreadCountAsync(userId, GetUserRole());
        return Ok(new { unreadCount = count });
    }

    // PATCH /api/notifications/{id}/read
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var userId = GetUserId();
        var result = await _notificationService.MarkAsReadAsync(id, userId);
        if (!result) return NotFound(new { success = false, message = "Notification not found." });
        return Ok(new { success = true });
    }

    // PATCH /api/notifications/read-all
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = GetUserId();
        await _notificationService.MarkAllAsReadAsync(userId, GetUserRole());
        return Ok(new { success = true });
    }

    // DELETE /api/notifications/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = GetUserId();
        var result = await _notificationService.DeleteAsync(id, userId);
        if (!result) return NotFound(new { success = false, message = "Notification not found." });
        return Ok(new { success = true });
    }

    // DELETE /api/notifications/clear-all
    [HttpDelete("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        var userId = GetUserId();
        await _notificationService.DeleteAllAsync(userId, GetUserRole());
        return Ok(new { success = true });
    }
}
