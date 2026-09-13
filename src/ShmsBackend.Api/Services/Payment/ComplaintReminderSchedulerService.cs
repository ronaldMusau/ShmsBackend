using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
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
        var superAdmins = await context.SuperAdmins.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
        var adminUsers = await context.AdminUsers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
        var managers = await context.Managers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
        var secretaries = await context.Secretaries.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
        var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();

        // Complaint has scalar TenantId/HouseId only (no Tenant/House nav properties — confirmed from
        // the entity), so tenant names and house numbers for the grouped-email rows are resolved via
        // bulk dictionary lookups, same pattern used elsewhere in this codebase for FK-only entities.
        var tenantIds = complaints.Select(c => c.TenantId).Distinct().ToList();
        var tenantNames = await context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var houseIds = complaints.Select(c => c.HouseId).Distinct().ToList();
        var houseNumbers = await context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        (string TicketNumber, string TenantName, string HouseNumber, int DaysOpen) ToItem(Complaint c) =>
            (c.TicketNumber, tenantNames.GetValueOrDefault(c.TenantId, "-"), houseNumbers.GetValueOrDefault(c.HouseId, "-"), (int)(now - c.CreatedAt).TotalDays);

        // Notifications and LastReminderSentAt stamping stay per-complaint — only the EMAIL sends
        // are batched below, to fix the same-recipient-gets-N-separate-emails-per-run problem.
        foreach (var complaint in complaints)
        {
            var daysOpen = (int)(now - complaint.CreatedAt).TotalDays;

            try
            {
                await notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                    $"Complaint {complaint.TicketNumber} has been open for {daysOpen} days and requires attention.",
                    "property", "Complaint", complaint.Id.ToString());
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send management notification for overdue complaint {TicketNumber}", complaint.TicketNumber); }

            if (complaint.EscalatedToAgentId.HasValue)
            {
                try { await notificationService.SendToUserAsync(complaint.EscalatedToAgentId.Value.ToString(), $"Complaint {complaint.TicketNumber} has been open for {daysOpen} days. Please complete your work.", "property", "Complaint", complaint.Id.ToString()); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of overdue complaint {TicketNumber}", complaint.TicketNumber); }
            }

            if (complaint.BillableTarget == "Management")
            {
                try { await notificationService.SendToUserAsync(complaint.LandlordId.ToString(), $"Complaint {complaint.TicketNumber} has been awaiting your decision for {daysOpen} days.", "property", "Complaint", complaint.Id.ToString()); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify landlord of overdue complaint {TicketNumber}", complaint.TicketNumber); }
            }

            complaint.LastReminderSentAt = now;
        }

        // Management — one grouped email per management user, carrying the SAME full list every
        // time (matches prior behavior's scope: every management user already saw every complaint;
        // only the delivery is batched, not the audience).
        var allItems = complaints.Select(ToItem).ToList();
        foreach (var mgr in managementUsers)
        {
            try { await emailService.SendComplaintOverdueManagementGroupedEmailAsync(mgr.Email, mgr.FirstName, allItems, mgr.Id.ToString(), false); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send grouped overdue complaint email to {Email}", mgr.Email); }
        }

        // Agent — one grouped email per distinct escalated agent, containing only their own complaints.
        var agentGroups = complaints.Where(c => c.EscalatedToAgentId.HasValue).GroupBy(c => c.EscalatedToAgentId!.Value);
        foreach (var group in agentGroups)
        {
            var agent = await context.Agents.FirstOrDefaultAsync(a => a.Id == group.Key);
            if (agent == null) continue;
            try { await emailService.SendComplaintOverdueAgentGroupedEmailAsync(agent.Email, agent.FirstName, group.Select(ToItem).ToList(), agent.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send grouped overdue complaint email to agent {Email}", agent.Email); }
        }

        // Landlord — one grouped email per distinct landlord, for complaints billable to Management
        // awaiting their decision. Complaint already carries its own scalar LandlordId column
        // directly (confirmed from the entity), so it's used as-is — no House/Flat join needed.
        var landlordGroups = complaints.Where(c => c.BillableTarget == "Management").GroupBy(c => c.LandlordId);
        foreach (var group in landlordGroups)
        {
            var landlord = await context.Landlords.FirstOrDefaultAsync(l => l.Id == group.Key);
            if (landlord == null) continue;
            try { await emailService.SendComplaintOverdueLandlordGroupedEmailAsync(landlord.Email, landlord.FirstName, group.Select(ToItem).ToList(), landlord.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send grouped overdue complaint email to landlord {Email}", landlord.Email); }
        }

        await context.SaveChangesAsync();
        _logger.LogInformation("Sent grouped reminders for {Count} overdue complaints", complaints.Count);
    }
}
