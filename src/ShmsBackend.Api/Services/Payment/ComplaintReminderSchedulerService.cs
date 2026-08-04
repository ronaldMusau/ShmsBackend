using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ShmsBackend.Api.Services.Payment;

public class ComplaintReminderSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ComplaintReminderSchedulerService> _logger;
    private DateTime? _lastRunDate;

    public ComplaintReminderSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<ComplaintReminderSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Complaint Reminder Scheduler Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Run complaint overdue reminders daily at 10:00
                if (now.Hour == 10 && now.Minute >= 0 && now.Minute < 2
                    && _lastRunDate?.Date != now.Date)
                {
                    await RunComplaintReminders();
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
                _logger.LogError(ex, "Error in Complaint Reminder Scheduler");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task RunComplaintReminders()
    {
        _logger.LogInformation("Running complaint overdue reminders");
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = DateTime.UtcNow;

        var allActiveComplaints = await context.Complaints
            .Include(c => c.ComplaintType)
            .Where(c => !c.IsDeleted && c.Status != "Closed")
            .ToListAsync();

        var complaints = allActiveComplaints.Where(c =>
            (now - c.CreatedAt).Days >= c.ComplaintType.ReminderDays &&
            (c.LastReminderSentAt == null || (now - c.LastReminderSentAt.Value).Days >= 3)
        ).ToList();

        if (!complaints.Any())
        {
            _logger.LogInformation("No overdue complaints require reminders");
            return;
        }

        // Fetch management users once before the loop
        var superAdmins = await context.SuperAdmins.Select(u => new { u.Email, u.FirstName }).ToListAsync();
        var adminUsers = await context.AdminUsers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
        var managers = await context.Managers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
        var secretaries = await context.Secretaries.Select(u => new { u.Email, u.FirstName }).ToListAsync();
        var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();

        foreach (var complaint in complaints)
        {
            var daysOpen = (int)(now - complaint.CreatedAt).TotalDays;

            // Management — notification + email to each user
            try
            {
                await notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                    $"Complaint {complaint.TicketNumber} has been open for {daysOpen} days and requires attention.",
                    "property");
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send management notification for overdue complaint {TicketNumber}", complaint.TicketNumber); }

            foreach (var mgr in managementUsers)
            {
                try { await emailService.SendComplaintOverdueManagementEmailAsync(mgr.Email, mgr.FirstName, complaint.TicketNumber, daysOpen); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send overdue complaint email to {Email}", mgr.Email); }
            }

            // Agent — if the complaint has been escalated
            if (complaint.EscalatedToAgentId.HasValue)
            {
                var agent = await context.Agents.FirstOrDefaultAsync(a => a.Id == complaint.EscalatedToAgentId.Value);
                if (agent != null)
                {
                    try { await emailService.SendComplaintOverdueAgentEmailAsync(agent.Email, agent.FirstName, complaint.TicketNumber, daysOpen); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to send overdue complaint email to agent {Email}", agent.Email); }
                }
                try { await notificationService.SendToUserAsync(complaint.EscalatedToAgentId.Value.ToString(), $"Complaint {complaint.TicketNumber} has been open for {daysOpen} days. Please complete your work.", "property"); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of overdue complaint {TicketNumber}", complaint.TicketNumber); }
            }

            // Landlord — if complaint is billable to management (awaiting their decision)
            if (complaint.BillableTarget == "Management")
            {
                var landlord = await context.Landlords.FirstOrDefaultAsync(l => l.Id == complaint.LandlordId);
                if (landlord != null)
                {
                    try { await emailService.SendComplaintOverdueLandlordEmailAsync(landlord.Email, landlord.FirstName, complaint.TicketNumber, daysOpen); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to send overdue complaint email to landlord {Email}", landlord.Email); }
                }
                try { await notificationService.SendToUserAsync(complaint.LandlordId.ToString(), $"Complaint {complaint.TicketNumber} has been awaiting your decision for {daysOpen} days.", "property"); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify landlord of overdue complaint {TicketNumber}", complaint.TicketNumber); }
            }

            complaint.LastReminderSentAt = now;
        }

        await context.SaveChangesAsync();
        _logger.LogInformation("Sent reminders for {Count} overdue complaints", complaints.Count);
    }
}
