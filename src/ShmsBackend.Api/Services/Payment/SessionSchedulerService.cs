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

    private async Task RunSessionSweep()
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

        foreach (var session in acceptedSessions)
        {
            session.Status = "AwaitingFeedback";
            session.FeedbackPromptSentAt = now;

            var explorer = await context.Explorers.FirstOrDefaultAsync(e => e.Id == session.ExplorerId);
            var house = await context.Houses.FirstOrDefaultAsync(h => h.Id == session.HouseId);
            var houseNumber = house?.HouseNumber ?? "";

            if (explorer != null)
            {
                try { await emailService.SendSessionFeedbackPromptEmailAsync(explorer.Email, explorer.FirstName, houseNumber, session.ScheduledAt); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send feedback prompt email to explorer {ExplorerId}", explorer.Id); }

                try { await notificationService.SendToUserAsync(explorer.Id.ToString(), $"Did your viewing session for house {houseNumber} take place? Please close the session or reschedule.", "property"); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify explorer for session feedback {SessionId}", session.Id); }
            }
        }

        if (acceptedSessions.Count > 0)
        {
            await context.SaveChangesAsync();
            _logger.LogInformation("Transitioned {Count} sessions to AwaitingFeedback", acceptedSessions.Count);
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
    }
}
