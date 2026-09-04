using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/overdue")]
[Authorize(Roles = "SuperAdmin,Admin,Manager,Secretary")]
public class OverdueController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<OverdueController> _logger;

    public OverdueController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<OverdueController> logger)
    {
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
        _logger = logger;
    }

    private Guid GetCallerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // GET /api/overdue/tenants
    [HttpGet("tenants")]
    public async Task<IActionResult> GetOverdueTenants()
    {
        var now = DateTime.UtcNow;

        var overduePayments = (await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.House)
                .ThenInclude(h => h!.Flat)
            .Where(p => (p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid
                    || p.PaymentStatus == PaymentTransactionStatus.Overdue)
                && p.DueDate < now
                && !p.IsDeleted)
            .ToListAsync())
            .Where(p => p.Tenant != null && p.TenancyCycle == p.Tenant.TenancyCycle)
            .ToList();

        var groups = overduePayments
            .GroupBy(p => p.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Tenant = g.First().Tenant!,
                House = g.First().House,
                OldestUnpaidDueDate = g.Min(p => p.DueDate),
                TotalArrears = g.Sum(p => p.Balance),
                MonthsOverdueCount = g.Count()
            })
            .Select(g => new
            {
                g.TenantId,
                g.Tenant,
                g.House,
                g.OldestUnpaidDueDate,
                g.TotalArrears,
                g.MonthsOverdueCount,
                OverdueDays = (int)(now - g.OldestUnpaidDueDate).TotalDays
            })
            .Where(g => g.OverdueDays > 0)
            .ToList();

        var tenantIds = groups.Select(g => g.TenantId).ToList();
        var warnings = await _context.TenantWarnings
            .Where(w => tenantIds.Contains(w.TenantId) && !w.IsDeleted)
            .ToListAsync();
        var warningsByTenant = warnings.GroupBy(w => w.TenantId).ToDictionary(g => g.Key, g => g.ToList());

        var activeVacateTenantIds = (await _context.VacateRequests
            .Where(v => tenantIds.Contains(v.TenantId) && v.Status != "Closed" && v.Status != "Cancelled" && !v.IsDeleted)
            .Select(v => v.TenantId)
            .ToListAsync())
            .ToHashSet();

        var data = groups.Select(g =>
        {
            warningsByTenant.TryGetValue(g.TenantId, out var tenantWarnings);
            var warning1 = tenantWarnings?.Where(w => w.WarningNumber == 1).OrderByDescending(w => w.SentAt).FirstOrDefault();
            var warning2 = tenantWarnings?.Where(w => w.WarningNumber == 2).OrderByDescending(w => w.SentAt).FirstOrDefault();

            return new
            {
                tenantId = g.TenantId,
                firstName = g.Tenant.FirstName,
                lastName = g.Tenant.LastName,
                phoneNumber = g.Tenant.PhoneNumber,
                email = g.Tenant.Email,
                flatName = g.House?.Flat?.FlatName ?? "-",
                houseNumber = g.House?.HouseNumber ?? "-",
                oldestUnpaidDueDate = g.OldestUnpaidDueDate,
                overdueDays = g.OverdueDays,
                totalArrears = g.TotalArrears,
                monthsOverdueCount = g.MonthsOverdueCount,
                warning1SentAt = warning1?.SentAt,
                warning2SentAt = warning2?.SentAt,
                canSendWarning1 = g.OverdueDays >= 45 && warning1 == null,
                canSendWarning2 = warning1 != null && warning2 == null,
                canForceVacate = warning2 != null && !activeVacateTenantIds.Contains(g.TenantId)
            };
        }).ToList();

        return Ok(new { success = true, data });
    }

    // GET /api/overdue/tenants/{tenantId}/breakdown
    [HttpGet("tenants/{tenantId:guid}/breakdown")]
    public async Task<IActionResult> GetOverdueBreakdown(Guid tenantId)
    {
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);
        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        var now = DateTime.UtcNow;

        var payments = await _context.Payments
            .Where(p => p.TenantId == tenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && (p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid
                    || p.PaymentStatus == PaymentTransactionStatus.Overdue)
                && p.DueDate < now)
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();

        var rows = payments.Select(p => new
        {
            monthLabel = new DateTime(p.Year, p.Month, 1).ToString("MMMM yyyy"),
            dueDate = p.DueDate,
            amount = p.Amount,
            amountPaid = p.AmountPaid,
            balance = p.Balance
        }).ToList();

        return Ok(new { success = true, data = rows, totalArrears = payments.Sum(p => p.Balance) });
    }

    // Recomputes arrears/overdueDays for one tenant, server-side, from the same predicate used above.
    private async Task<(decimal Arrears, int OverdueDays)?> ComputeOverdueSnapshotAsync(Guid tenantId)
    {
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);
        if (tenant == null) return null;

        var now = DateTime.UtcNow;

        var payments = await _context.Payments
            .Where(p => p.TenantId == tenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && (p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid
                    || p.PaymentStatus == PaymentTransactionStatus.Overdue)
                && p.DueDate < now)
            .ToListAsync();

        if (payments.Count == 0)
            return (0m, 0);

        var oldestDueDate = payments.Min(p => p.DueDate);
        var overdueDays = (int)(now - oldestDueDate).TotalDays;
        var totalArrears = payments.Sum(p => p.Balance);

        return (totalArrears, overdueDays);
    }

    // POST /api/overdue/tenants/{tenantId}/warning
    [HttpPost("tenants/{tenantId:guid}/warning")]
    public async Task<IActionResult> SendWarning(Guid tenantId, [FromBody] SendTenantWarningDto dto)
    {
        if (dto.WarningNumber != 1 && dto.WarningNumber != 2)
            return BadRequest(new { success = false, message = "warningNumber must be 1 or 2." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);
        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        var snapshot = await ComputeOverdueSnapshotAsync(tenantId);
        if (snapshot == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        var (arrears, overdueDays) = snapshot.Value;

        var warning1Exists = await _context.TenantWarnings
            .AnyAsync(w => w.TenantId == tenantId && w.WarningNumber == 1 && !w.IsDeleted);
        var warning2Exists = await _context.TenantWarnings
            .AnyAsync(w => w.TenantId == tenantId && w.WarningNumber == 2 && !w.IsDeleted);

        if (dto.WarningNumber == 1)
        {
            if (warning1Exists)
                return BadRequest(new { success = false, message = "A first warning has already been sent to this tenant." });
            if (overdueDays < 45)
                return BadRequest(new { success = false, message = "This tenant is not yet overdue by 45 days or more." });
        }
        else
        {
            if (!warning1Exists)
                return BadRequest(new { success = false, message = "A first warning must be sent before a second warning." });
            if (warning2Exists)
                return BadRequest(new { success = false, message = "A second warning has already been sent to this tenant." });
        }

        var warning = new TenantWarning
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarningNumber = dto.WarningNumber,
            SentAt = DateTime.UtcNow,
            SentByAdminId = GetCallerId(),
            ArrearsAtTime = arrears,
            OverdueDaysAtTime = overdueDays
        };

        _context.TenantWarnings.Add(warning);
        await _context.SaveChangesAsync();

        if (dto.WarningNumber == 1)
        {
            try { await _emailService.SendFirstWarningToVacateEmailAsync(tenant.Email, tenant.FirstName, arrears, overdueDays, tenant.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send first warning email to tenant {TenantId}", tenantId); }

            try { await _notificationService.SendToUserAsync(tenant.Id.ToString(), $"You have KES {arrears:N2} in arrears, overdue by {overdueDays} day(s). Please settle this balance.", "rent"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send first warning notification to tenant {TenantId}", tenantId); }
        }
        else
        {
            try { await _emailService.SendFinalWarningToVacateEmailAsync(tenant.Email, tenant.FirstName, arrears, overdueDays, tenant.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send final warning email to tenant {TenantId}", tenantId); }

            try { await _notificationService.SendToUserAsync(tenant.Id.ToString(), $"Final notice: KES {arrears:N2} in arrears, overdue by {overdueDays} day(s). Continued non-payment may result in your tenancy being terminated.", "rent"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send final warning notification to tenant {TenantId}", tenantId); }
        }

        return Ok(new { success = true });
    }

    // POST /api/overdue/tenants/{tenantId}/force-vacate
    [HttpPost("tenants/{tenantId:guid}/force-vacate")]
    public async Task<IActionResult> ForceVacate(Guid tenantId, [FromBody] ForceVacateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new { success = false, message = "A reason is required." });

        var warning2Exists = await _context.TenantWarnings
            .AnyAsync(w => w.TenantId == tenantId && w.WarningNumber == 2 && !w.IsDeleted);
        if (!warning2Exists)
            return BadRequest(new { success = false, message = "This tenant has not received a second warning yet." });

        var hasActiveVacateRequest = await _context.VacateRequests
            .AnyAsync(v => v.TenantId == tenantId && v.Status != "Closed" && v.Status != "Cancelled" && !v.IsDeleted);
        if (hasActiveVacateRequest)
            return BadRequest(new { success = false, message = "An active vacate request already exists for this tenant." });

        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);

        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (tenant.House == null || tenant.House.Flat == null)
            return BadRequest(new { success = false, message = "Tenant is not assigned to a house." });

        var now = DateTime.UtcNow;
        var nextMonth = now.Month == 12 ? 1 : now.Month + 1;
        var nextYear = now.Month == 12 ? now.Year + 1 : now.Year;

        var agentAssignment = await _context.AgentFlats
            .Include(af => af.Agent)
            .FirstOrDefaultAsync(af => af.FlatId == tenant.House.FlatId);
        var agent = agentAssignment?.Agent;

        var vacateRequest = new VacateRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            HouseId = tenant.House.Id,
            FlatId = tenant.House.Flat.Id,
            LandlordId = tenant.House.Flat.LandlordId,
            Status = "Open",
            VacateMonth = nextMonth,
            VacateYear = nextYear,
            SitDeposit = tenant.House.Flat.SitDeposit,
            AssignedAgentId = agent?.Id,
            InspectionAssignedAt = agent != null ? DateTime.UtcNow : null,
            Reason = dto.Reason,
            InitiationType = "Delinquency",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.VacateRequests.Add(vacateRequest);
        await _context.SaveChangesAsync();

        var houseNumber = tenant.House.HouseNumber;

        // Same agent + management notifications as VacateController.CreateRequest.
        if (agent != null)
        {
            try { await _emailService.SendVacateAssignedAgentEmailAsync(agent.Email, agent.FirstName, houseNumber, agent.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send vacate inspection email to agent {AgentId}", agent.Id); }

            try { await _notificationService.SendToUserAsync(agent.Id.ToString(), $"You have a new vacate inspection assigned for house {houseNumber}.", "property", "Vacate", vacateRequest.Id.ToString()); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of vacate inspection assignment"); }
        }
        else
        {
            try
            {
                await _notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                    $"Vacate request for House {houseNumber} (non-payment) has no agent assigned — please assign one to begin inspection.",
                    "property", "Vacate", vacateRequest.Id.ToString());
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of unassigned force-vacate request"); }
        }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"A new vacate request has been raised for house {houseNumber}.",
                "property", "Vacate", vacateRequest.Id.ToString());
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of new vacate request"); }

        // Tenant-facing notice: management initiated this vacate due to non-payment.
        try { await _emailService.SendForcedVacateNoticeEmailAsync(tenant.Email, tenant.FirstName, houseNumber, dto.Reason, nextMonth, nextYear, tenant.Id.ToString(), true); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send forced vacate notice email to tenant {TenantId}", tenantId); }

        try { await _notificationService.SendToUserAsync(tenant.Id.ToString(), $"Management has initiated the vacate process for your unit due to non-payment. Reason: {dto.Reason}", "property", "Vacate", vacateRequest.Id.ToString()); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of forced vacate"); }

        return Ok(new { success = true, data = new { vacateRequest.Id } });
    }
}

public class SendTenantWarningDto
{
    public int WarningNumber { get; set; }
}

public class ForceVacateDto
{
    public string Reason { get; set; } = string.Empty;
}
