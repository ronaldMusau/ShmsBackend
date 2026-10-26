using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace ShmsBackend.Api.Services.Payment;

public class SessionSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionSchedulerService> _logger;
    private DateTime? _lastRunDate;

    public SessionSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<SessionSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Session Scheduler Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Run session lifecycle sweep daily at 11:00
                if (now.Hour == 11 && now.Minute >= 0 && now.Minute < 2
                    && _lastRunDate?.Date != now.Date)
                {
                    await RunSessionSweep();
                    _lastRunDate = now.Date;
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Session Scheduler");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    public async Task RunSessionSweep()
    {
        _logger.LogInformation("Running session lifecycle sweep");
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = DateTime.UtcNow;

        // Pass A — Accepted → AwaitingFeedback (scheduled time has passed)
        var acceptedSessions = await context.ListingViewingSessions
            .Where(s => s.Status == "Accepted" && s.ScheduledAt < now)
            .ToListAsync();

        var explorerIds = acceptedSessions.Select(s => s.ExplorerId).Distinct().ToList();
        var explorers = await context.Explorers
            .Where(e => explorerIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e);

        var sessionHouseIds = acceptedSessions.Select(s => s.HouseId).Distinct().ToList();
        var houseNumbers = await context.Houses
            .Where(h => sessionHouseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        foreach (var session in acceptedSessions)
        {
            session.Status = "AwaitingFeedback";
            session.FeedbackPromptSentAt = now;

            var houseNumber = houseNumbers.GetValueOrDefault(session.HouseId, "");

            try { await notificationService.SendToUserAsync(session.ExplorerId.ToString(), $"Did your viewing session for house {houseNumber} take place? Please close the session or reschedule.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify explorer for session feedback {SessionId}", session.Id); }
        }

        if (acceptedSessions.Count > 0)
        {
            await context.SaveChangesAsync();
            _logger.LogInformation("Transitioned {Count} sessions to AwaitingFeedback", acceptedSessions.Count);
        }

        // Grouped feedback-prompt emails — one per distinct explorer, containing every session of
        // theirs that transitioned in this sweep, instead of one email per session.
        var explorerGroups = acceptedSessions.GroupBy(s => s.ExplorerId);
        foreach (var group in explorerGroups)
        {
            if (!explorers.TryGetValue(group.Key, out var explorer)) continue;

            var items = group.Select(s => (
                HouseNumber: houseNumbers.GetValueOrDefault(s.HouseId, ""),
                ScheduledAt: s.ScheduledAt
            )).ToList();

            try { await emailService.SendSessionFeedbackPromptGroupedEmailAsync(explorer.Email, explorer.FirstName, items, explorer.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send grouped feedback prompt email to explorer {ExplorerId}", explorer.Id); }
        }

        // Pass B — AwaitingFeedback → Forfeited (24hr timeout, silent terminal state)
        var forfeitCutoff = now.AddHours(-24);
        var awaitingSessions = await context.ListingViewingSessions
            .Where(s => s.Status == "AwaitingFeedback" && s.FeedbackPromptSentAt < forfeitCutoff)
            .ToListAsync();

        foreach (var session in awaitingSessions)
            session.Status = "Forfeited";

        if (awaitingSessions.Count > 0)
        {
            await context.SaveChangesAsync();
            _logger.LogInformation("Forfeited {Count} sessions that exceeded the 24hr feedback window", awaitingSessions.Count);
        }

        // Pass C — compiled reminder for sessions still needing reassignment.
        await RunReassignmentReminders();
    }

    /// <summary>
    /// One compiled, forced (preference-bypassing) alert per management user listing every agent who needs
    /// reassigning: agents over the daily session limit on an upcoming day, and sessions an agent declined.
    /// Returns how many sessions are waiting (0 = nothing sent).
    /// </summary>
    public async Task<int> RunReassignmentReminders()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var cap = await context.CompanySettings.Select(c => (int?)c.MaxSessionsPerAgentPerDay).FirstOrDefaultAsync();
        var maxPerDay = cap is > 0 ? cap.Value : 5;
        var today = DateTime.UtcNow.Date;

        var live = await context.ListingViewingSessions
            .Where(s => s.ScheduledAt >= today && (s.Status == "Pending" || s.Status == "Accepted" || s.Status == "AwaitingFeedback"))
            .Select(s => new { s.AgentId, Day = s.ScheduledAt.Date })
            .ToListAsync();
        var overloaded = live
            .GroupBy(x => new { x.AgentId, x.Day })
            .Where(g => g.Count() > maxPerDay)
            .Select(g => new { g.Key.AgentId, g.Key.Day, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToList();

        var declined = await context.ListingViewingSessions
            .Where(s => s.Status == "Declined" && s.ScheduledAt >= today)
            .Select(s => new { s.AgentId, s.HouseId, s.ScheduledAt })
            .ToListAsync();

        var waiting = overloaded.Count + declined.Count;
        if (waiting == 0) return 0;

        var agentIds = overloaded.Select(o => o.AgentId).Concat(declined.Select(d => d.AgentId)).Distinct().ToList();
        var agentNames = await context.Agents
            .Where(a => agentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => $"{a.FirstName} {a.LastName}".Trim());
        var houseIds = declined.Select(d => d.HouseId).Distinct().ToList();
        var houseNumbers = await context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var lines = new List<string>();
        foreach (var o in overloaded)
            lines.Add($"{agentNames.GetValueOrDefault(o.AgentId, "An agent")} has {o.Count} sessions on {o.Day:yyyy-MM-dd} (limit {maxPerDay})");
        foreach (var d in declined)
            lines.Add($"{agentNames.GetValueOrDefault(d.AgentId, "An agent")} declined house {houseNumbers.GetValueOrDefault(d.HouseId, "")} on {d.ScheduledAt:yyyy-MM-dd}");

        var message = $"{waiting} viewing-session item(s) need reassigning: {string.Join("; ", lines.Take(5))}{(lines.Count > 5 ? $"; +{lines.Count - 5} more" : "")}.";

        var superAdmins = await context.SuperAdmins.Select(u => u.Id).ToListAsync();
        var adminUsers = await context.AdminUsers.Select(u => u.Id).ToListAsync();
        var managers = await context.Managers.Select(u => u.Id).ToListAsync();
        var secretaries = await context.Secretaries.Select(u => u.Id).ToListAsync();
        foreach (var id in superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).Distinct())
        {
            try { await notificationService.SendForcedToUserAsync(id.ToString(), message, "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send reassignment reminder to management user {UserId}", id); }
        }

        _logger.LogInformation("Sent reassignment reminder for {Count} item(s)", waiting);
        return waiting;
    }
}
