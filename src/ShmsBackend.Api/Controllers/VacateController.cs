using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/vacate")]
[Authorize]
public class VacateController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<VacateController> _logger;

    public VacateController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<VacateController> logger)
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

    // GET /api/vacate/eligibility/{tenantId}
    [HttpGet("eligibility/{tenantId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Tenant")]
    public async Task<IActionResult> GetEligibility(Guid tenantId)
    {
        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (callerRole == "Tenant" && GetCallerId() != tenantId)
            return Forbid();

        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);

        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (tenant.House == null || tenant.House.Flat == null)
            return BadRequest(new { success = false, message = "Tenant is not assigned to a house." });

        var arrears = await _context.Payments
            .Where(p => p.TenantId == tenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && (p.PaymentStatus == PaymentTransactionStatus.Overdue
                    || p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid))
            .SumAsync(p => p.Balance);

        var totalPaid = await _context.Payments
            .Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .SumAsync(p => p.AmountPaid);

        var totalDue = await _context.Payments
            .Where(p => p.TenantId == tenantId
                && !p.IsDeleted
                && p.PaymentStatus != PaymentTransactionStatus.Cancelled
                && p.PaymentStatus != PaymentTransactionStatus.Failed)
            .SumAsync(p => p.Amount);

        var advanceCredit = Math.Max(0, totalPaid - totalDue);

        var today = DateTime.UtcNow.Day;
        var deadlineDay = tenant.House.Flat.VacateNoticeDeadlineDay;
        var windowOpen = today <= deadlineDay;

        return Ok(new
        {
            success = true,
            data = new
            {
                hasArrears = arrears > 0,
                arrearsAmount = arrears,
                hasAdvanceCredit = advanceCredit > 0,
                advanceCreditAmount = advanceCredit,
                windowOpen,
                vacateNoticeDeadlineDay = deadlineDay,
                sitDeposit = tenant.House.Flat.SitDeposit,
                depositFee = tenant.House.DepositFee
            }
        });
    }

    // POST /api/vacate/request
    [HttpPost("request")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Agent,Tenant")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateVacateRequestDto dto)
    {
        var callerId = GetCallerId();
        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        if (callerRole == "Tenant" && callerId != dto.TenantId)
            return Forbid();

        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == dto.TenantId && !t.IsDeleted);

        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (tenant.House == null || tenant.House.Flat == null)
            return BadRequest(new { success = false, message = "Tenant is not assigned to a house." });

        if (callerRole == "Tenant" && DateTime.UtcNow.Day > tenant.House.Flat.VacateNoticeDeadlineDay)
            return BadRequest(new { success = false, message = "Vacate notice window has closed for this month." });

        var arrears = await _context.Payments
            .Where(p => p.TenantId == dto.TenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && (p.PaymentStatus == PaymentTransactionStatus.Overdue
                    || p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid))
            .SumAsync(p => p.Balance);

        if (arrears > 0)
            return BadRequest(new
            {
                success = false,
                errorType = "ArrearsBlock",
                arrearsAmount = arrears,
                message = "Outstanding arrears must be cleared before vacating."
            });

        if (callerRole == "Tenant")
        {
            var totalPaid = await _context.Payments
                .Where(p => p.TenantId == dto.TenantId && !p.IsDeleted)
                .SumAsync(p => p.AmountPaid);

            var totalDue = await _context.Payments
                .Where(p => p.TenantId == dto.TenantId
                    && !p.IsDeleted
                    && p.PaymentStatus != PaymentTransactionStatus.Cancelled
                    && p.PaymentStatus != PaymentTransactionStatus.Failed)
                .SumAsync(p => p.Amount);

            var advanceCredit = Math.Max(0, totalPaid - totalDue);
            if (advanceCredit > 0)
                return BadRequest(new
                {
                    success = false,
                    errorType = "AdvanceCreditBlock",
                    advanceCreditAmount = advanceCredit,
                    message = "You have a credit balance — please contact management to arrange vacating; advance payments are not refunded."
                });
        }

        DateTime requestDate;
        try { requestDate = new DateTime(dto.VacateYear, dto.VacateMonth, 1); }
        catch { return BadRequest(new { success = false, message = "Invalid vacate month or year." }); }

        var now = DateTime.UtcNow;
        var minDate = new DateTime(now.Year, now.Month, 1);
        var maxDate = minDate.AddMonths(5);
        if (requestDate < minDate || requestDate > maxDate)
            return BadRequest(new { success = false, message = "Vacate month must be between now and 5 months from today." });

        var existingRequest = await _context.VacateRequests.AnyAsync(r =>
            r.TenantId == dto.TenantId && r.Status != "Closed" && !r.IsDeleted);
        if (existingRequest)
            return BadRequest(new { success = false, message = "A vacate request already exists for this tenancy." });

        var agentAssignment = await _context.AgentFlats
            .Include(af => af.Agent)
            .FirstOrDefaultAsync(af => af.FlatId == tenant.House.FlatId);

        if (agentAssignment == null)
            return BadRequest(new
            {
                success = false,
                errorType = "NoAgentAssigned",
                message = "No agent is currently assigned to this flat. Please raise a complaint or contact management before proceeding with vacating."
            });

        var vacateRequest = new VacateRequest
        {
            Id = Guid.NewGuid(),
            TenantId = dto.TenantId,
            HouseId = tenant.House.Id,
            FlatId = tenant.House.Flat.Id,
            LandlordId = tenant.House.Flat.LandlordId,
            Status = "Open",
            VacateMonth = dto.VacateMonth,
            VacateYear = dto.VacateYear,
            SitDeposit = tenant.House.Flat.SitDeposit,
            AssignedAgentId = agentAssignment.AgentId,
            InspectionAssignedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.VacateRequests.Add(vacateRequest);
        await _context.SaveChangesAsync();

        var agent = agentAssignment.Agent;
        var houseNumber = tenant.House.HouseNumber;

        try { await _emailService.SendVacateAssignedAgentEmailAsync(agent.Email, agent.FirstName, houseNumber); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send vacate inspection email to agent {AgentId}", agent.Id); }

        try { await _notificationService.SendToUserAsync(agent.Id.ToString(), $"You have a new vacate inspection assigned for house {houseNumber}.", "property"); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of vacate inspection assignment"); }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"A new vacate request has been raised for house {houseNumber}.",
                "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of new vacate request"); }

        return Ok(new { success = true, data = new { vacateRequest.Id } });
    }
}

public class CreateVacateRequestDto
{
    public Guid TenantId { get; set; }
    public int VacateMonth { get; set; }
    public int VacateYear { get; set; }
}
