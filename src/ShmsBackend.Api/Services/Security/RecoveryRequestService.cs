using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Security;

public interface IRecoveryRequestService
{
    /// <summary>
    /// Someone who cannot sign in (lost phone, no recovery codes) asks an administrator to reset their authenticator.
    /// Nothing is reset here and the caller learns nothing about whether the email exists.
    /// </summary>
    Task RequestAsync(string email, string? ip);
}

public class RecoveryRequestService : IRecoveryRequestService
{
    private readonly ShmsDbContext _db;
    private readonly ISecurityEventService _events;
    private readonly IThreatGuard _guard;
    public RecoveryRequestService(ShmsDbContext db, ISecurityEventService events, IThreatGuard guard)
    { _db = db; _events = events; _guard = guard; }

    public async Task RequestAsync(string email, string? ip)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        if (email.Length is < 5 or > 200 || !email.Contains('@')) return;

        var s = await _guard.GetSettingsAsync();
        var cooldown = TimeSpan.FromMinutes(s.RecoveryRequestCooldownMinutes > 0 ? s.RecoveryRequestCooldownMinutes : 60);
        var now = DateTime.UtcNow;

        // Only enrolled, active staff accounts can need an authenticator reset.
        var accounts = await _db.Admins.Where(a => a.Email.ToLower() == email && a.IsActive && !a.IsDeleted && a.TotpEnabledAt != null).ToListAsync();
        foreach (var a in accounts)
        {
            if (a.RecoveryRequestedAt != null && now - a.RecoveryRequestedAt < cooldown) continue;
            a.RecoveryRequestedAt = now;
            await _db.SaveChangesAsync();
            await _events.RecordAsync("Access", "RecoveryRequested", "High",
                $"{a.FirstName} {a.LastName} ({a.UserType}, {a.Email}) asked for an authenticator reset from IP {ip}. Confirm it is really them (for example by phone), then reset it on the Authenticators tab.",
                a.Id.ToString(), a.UserType.ToString(), ip, "Account", a.Id, alsoEmail: true);
        }
    }
}
