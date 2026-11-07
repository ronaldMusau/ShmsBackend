using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Security;

public interface ISecurityEventService
{
    /// <summary>Records an event. Never throws. High/Critical also push to every SuperAdmin and Admin.</summary>
    Task RecordAsync(string source, string eventType, string severity, string summary,
        string? actorUserId = null, string? actorRole = null, string? ip = null,
        string? entityType = null, Guid? entityId = null, bool alsoEmail = false);

    /// <summary>
    /// Deactivates an account suspected of leaking data or being taken over, then records a Critical
    /// event (which pushes to every SuperAdmin and Admin). SuperAdmin accounts are never auto-deactivated,
    /// only alerted on, so the owner can't be locked out by a false alarm. Returns true if deactivated.
    /// </summary>
    Task<bool> DeactivateAccountAsync(string userId, string reason, string source, string eventType, string? ip = null);

    /// <summary>
    /// Sends one forced (preference-bypassing) notification to every active user of every portal, in the
    /// background so the caller is not kept waiting. Used for "system is down" / "system is back" notices.
    /// </summary>
    void BroadcastToEveryone(string message, string category = "system");
}

public class SecurityEventService : ISecurityEventService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SecurityEventService> _log;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

    public SecurityEventService(IServiceScopeFactory scopes, ILogger<SecurityEventService> log,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    { _scopes = scopes; _log = log; _cache = cache; }

    public async Task RecordAsync(string source, string eventType, string severity, string summary,
        string? actorUserId = null, string? actorRole = null, string? ip = null,
        string? entityType = null, Guid? entityId = null, bool alsoEmail = false)
    {
        try
        {
            // Own scope: the caller's DbContext may be mid-transaction or already failed.
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();

            if (summary.Length > 500) summary = summary[..500];
            db.SecurityEvents.Add(new SecurityEvent
            {
                Source = source, EventType = eventType, Severity = severity, Summary = summary,
                ActorUserId = actorUserId, ActorRole = actorRole, IpAddress = ip,
                EntityType = entityType, EntityId = entityId
            });
            await db.SaveChangesAsync();

            if (severity is "High" or "Critical")
            {
                var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var ids = new List<string>();
                ids.AddRange((await db.SuperAdmins.AsNoTracking().Select(u => u.Id).ToListAsync()).Select(i => i.ToString()));
                ids.AddRange((await db.AdminUsers.AsNoTracking().Select(u => u.Id).ToListAsync()).Select(i => i.ToString()));
                // Forced: security alerts ignore each person's mute / preference toggles.
                foreach (var id in ids)
                    await notifier.SendForcedToUserAsync(id, $"Security alert: {summary}", "security", "SecurityEvent", null);

                // Critical alerts are also emailed (third channel). Never gated by preferences.
                if (severity == "Critical" || alsoEmail)
                {
                    var mail = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var people = await db.Admins.AsNoTracking()
                        .Where(a => a.IsActive && !a.IsDeleted && (a.UserType == ShmsBackend.Data.Enums.UserType.SuperAdmin || a.UserType == ShmsBackend.Data.Enums.UserType.Admin))
                        .Select(a => new { a.Email, a.FirstName }).ToListAsync();
                    foreach (var p in people)
                        try { await mail.SendSecurityAlertEmailAsync(p.Email, p.FirstName, summary, DateTime.UtcNow); }
                        catch (Exception ex) { _log.LogWarning(ex, "Security alert email failed"); }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not record security event {EventType}", eventType);
        }
    }

    public async Task<bool> DeactivateAccountAsync(string userId, string reason, string source, string eventType, string? ip = null)
    {
        var done = false;
        string? role = null;
        try
        {
            if (Guid.TryParse(userId, out var id))
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();

                var admin = await db.Admins.FirstOrDefaultAsync(a => a.Id == id);
                if (admin != null)
                {
                    if (admin is SuperAdmin) role = "SuperAdmin";
                    else
                    {
                        role = "Staff";
                        admin.IsActive = false;
                        admin.RefreshToken = null;
                        admin.RefreshTokenExpiryTime = null;
                        await db.SaveChangesAsync();
                        done = true;
                    }
                }
                else
                {
                    var portal = await db.PortalUsers.FirstOrDefaultAsync(u => u.Id == id);
                    if (portal != null)
                    {
                        role = "Portal user";
                        portal.IsActive = false;
                        portal.RefreshToken = null;
                        portal.RefreshTokenExpiryTime = null;
                        await db.SaveChangesAsync();
                        done = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not deactivate account {UserId}", userId);
        }
        // Take effect on the very next request instead of waiting for the 60-second active-account cache.
        if (done) _cache.Remove($"auth:active:{userId}");

        var manual = eventType.EndsWith("Manual", StringComparison.Ordinal);
        var outcome = done
            ? (manual ? "Account deactivated." : "Account deactivated automatically.")
            : "Account was NOT deactivated (SuperAdmin or not found) - review it now.";
        // A person's own decision is logged but does not page every admin; an automatic deactivation does.
        await RecordAsync(source, eventType, done && manual ? "Warning" : "Critical", $"{reason} {outcome}", userId, role, ip);
        return done;
    }

    public void BroadcastToEveryone(string message, string category = "system")
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var ids = new List<string>();
                ids.AddRange((await db.Admins.AsNoTracking().Where(a => a.IsActive && !a.IsDeleted).Select(a => a.Id).ToListAsync()).Select(i => i.ToString()));
                ids.AddRange((await db.PortalUsers.AsNoTracking().Where(u => u.IsActive).Select(u => u.Id).ToListAsync()).Select(i => i.ToString()));
                var sent = 0;
                foreach (var id in ids)
                {
                    try { await notifier.SendForcedToUserAsync(id, message, category, "System", null); sent++; }
                    catch (Exception ex) { _log.LogWarning(ex, "Broadcast notification failed for one user"); }
                }
                _log.LogInformation("System notice sent to {Count} of {Total} users", sent, ids.Count);
            }
            catch (Exception ex) { _log.LogError(ex, "System-wide notice failed"); }
        });
    }
}
