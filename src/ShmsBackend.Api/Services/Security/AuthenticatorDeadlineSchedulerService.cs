using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Services.Security;

/// <summary>
/// Deactivates staff who have not activated the authenticator app within the deadline set in the Security Centre
/// (days from account creation, or from the last re-activation or authenticator reset). SuperAdmin is never deactivated.
/// A deadline of 0 switches the rule off. A deactivated account can be re-activated, which starts a fresh window.
/// </summary>
public class AuthenticatorDeadlineSchedulerService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AuthenticatorDeadlineSchedulerService> _log;

    public AuthenticatorDeadlineSchedulerService(IServiceScopeFactory scopes, ILogger<AuthenticatorDeadlineSchedulerService> log)
    { _scopes = scopes; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Short pause so the app finishes starting (and migrations apply) before the first check.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(); }
            catch (Exception ex) { _log.LogError(ex, "Authenticator deadline check failed"); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<ISecurityEventService>();

        var days = await db.SecuritySettings.AsNoTracking().OrderBy(s => s.Id).Select(s => (int?)s.AuthenticatorDeadlineDays).FirstOrDefaultAsync() ?? 7;
        if (days <= 0) return;
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var late = await db.Admins.AsNoTracking()
            .Where(a => !a.IsDeleted && a.IsActive && a.TotpEnabledAt == null && a.UserType != UserType.SuperAdmin
                        && (a.AuthenticatorGraceFrom ?? a.CreatedAt) < cutoff)
            .Select(a => new { a.Id, a.Email, a.UserType }).ToListAsync();

        foreach (var a in late)
        {
            var done = await events.DeactivateAccountAsync(a.Id.ToString(),
                $"The {a.UserType} account {a.Email} did not set up the authenticator app within {days} days.", "Access", "AuthenticatorDeadline");
            if (done) _log.LogWarning("Deactivated {Email}: authenticator not set up within {Days} days", a.Email, days);
        }
    }
}
